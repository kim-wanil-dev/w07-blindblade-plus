using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlindBlade.Telemetry
{
    /// <summary>원본 JSONL과 전송 커서를 직렬화된 파일 작업으로 관리한다. Unity API를 호출하지 않는다.</summary>
    internal sealed class TelemetryDiskStore
    {
        private readonly object _gate = new object();
        private readonly string _root;
        private readonly long _maximumBytes;
        private long _writtenBytes;
        private bool _initialized;
        private string _readError;

        internal string Root => _root;
        internal string ReadError => _readError;

        /// <summary>root와 maximumBytes를 보관한다. 실제 디렉터리 접근은 백그라운드 작업에서 수행한다.</summary>
        internal TelemetryDiskStore(string root, long maximumBytes)
        {
            _root = root;
            _maximumBytes = maximumBytes;
        }

        /// <summary>records를 세션별 JSONL에 추가하고 최초 메타데이터를 JSON으로 저장한다. 실패는 Task 오류로 반환한다.</summary>
        internal Task AppendAsync(string[] records)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    Initialize();
                    foreach (IGrouping<string, string> group in records.GroupBy(line => (string)JObject.Parse(line)["sessionId"]))
                    {
                        string content = string.Join("\n", group) + "\n";
                        long bytes = Encoding.UTF8.GetByteCount(content);
                        if (_writtenBytes + bytes > _maximumBytes)
                            throw new IOException("로컬 로그 용량 한도에 도달했습니다. 원본을 백업한 뒤 공간을 확보하세요.");
                        string path = Path.Combine(_root, group.Key + ".jsonl");
                        using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        {
                            byte[] data = Encoding.UTF8.GetBytes(content);
                            stream.Write(data, 0, data.Length);
                            stream.Flush(true);
                        }
                        _writtenBytes += bytes;
                        string metadataPath = Path.Combine(_root, group.Key + ".json");
                        if (!File.Exists(metadataPath))
                        {
                            JObject first = JObject.Parse(group.First());
                            File.WriteAllText(metadataPath, first.ToString(Formatting.Indented), new UTF8Encoding(false));
                        }
                    }
                }
            });
        }

        /// <summary>destination별 커서 뒤의 완전한 줄을 limit·byteLimit 안에서 읽어 한 파일의 배치를 반환한다. 원본은 변경하지 않는다.</summary>
        internal Task<UploadBatch> ReadBatchAsync(int limit, int byteLimit, string destination)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    Initialize();
                    _readError = null;
                    foreach (string path in Directory.GetFiles(_root, "*.jsonl").OrderBy(File.GetLastWriteTimeUtc))
                    {
                        try
                        {
                            // 이전 빌드의 세션은 새 수신기에 재전송하지 않는다.
                            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
                            {
                                string first = reader.ReadLine();
                                if (string.IsNullOrWhiteSpace(first)
                                    || (string)JObject.Parse(first)["schemaVersion"] != TelemetryJson.SCHEMA_VERSION)
                                    continue;
                            }
                            string cursorPath = path + "." + destination + ".cursor";
                            long offset = File.Exists(cursorPath) ? long.Parse(File.ReadAllText(cursorPath)) : 0L;
                            JArray rows = new JArray();
                            long end = offset;
                            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                if (offset < 0 || offset > stream.Length)
                                    throw new IOException("전송 커서가 원본 범위를 벗어났습니다: " + Path.GetFileName(path));
                                stream.Position = offset;
                                using (MemoryStream line = new MemoryStream())
                                {
                                    while (stream.Position < stream.Length && rows.Count < limit)
                                    {
                                        int value = stream.ReadByte();
                                        if (value != '\n')
                                        {
                                            line.WriteByte((byte)value);
                                            if (line.Length > byteLimit)
                                                throw new IOException("JSONL 한 줄이 전송 크기 한도를 넘었습니다: " + Path.GetFileName(path));
                                            continue;
                                        }
                                        if (stream.Position - offset > byteLimit && rows.Count > 0) break;
                                        try { rows.Add(JObject.Parse(Encoding.UTF8.GetString(line.ToArray()))); }
                                        catch (JsonException)
                                        {
                                            if (rows.Count > 0) break;
                                            throw;
                                        }
                                        line.SetLength(0);
                                        end = stream.Position;
                                    }
                                    // 강제 종료로 잘린 마지막 줄은 확인 응답 대상에 포함하지 않는다.
                                    if (line.Length > 0 && rows.Count == 0)
                                        throw new IOException("불완전한 JSONL 끝줄이 있습니다. 전처리 보고서로 확인하세요: " + Path.GetFileName(path));
                                }
                            }
                            if (rows.Count > 0)
                                return new UploadBatch(cursorPath, end, rows);
                        }
                        catch (Exception exception) when (exception is IOException || exception is JsonException || exception is FormatException)
                        {
                            _readError = "원본 전송 보류: " + Path.GetFileName(path) + " / " + exception.Message;
                        }
                    }
                    return null;
                }
            });
        }

        /// <summary>서버가 확인한 batch의 끝 위치를 원자적 교체로 저장한다. 실패하면 같은 사건을 다시 보낸다.</summary>
        internal Task AcknowledgeAsync(UploadBatch batch)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    string path = batch.CursorPath;
                    string temporary = path + ".tmp";
                    File.WriteAllText(temporary, batch.EndOffset.ToString(), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
            });
        }

        /// <summary>전용 폴더를 준비하고 기존 JSONL 용량을 한 번 집계한다. 호출자는 _gate를 소유한다.</summary>
        private void Initialize()
        {
            if (_initialized) return;
            Directory.CreateDirectory(_root);
            _writtenBytes = Directory.GetFiles(_root, "*.jsonl").Sum(path => new FileInfo(path).Length);
            _initialized = true;
        }
    }

    /// <summary>한 번의 확인 응답에 대응하는 파일 범위와 불변 전송 데이터를 보관한다.</summary>
    internal sealed class UploadBatch
    {
        internal string CursorPath { get; }
        internal long EndOffset { get; }
        internal JArray Records { get; }
        internal string BatchId { get; }

        /// <summary>cursorPath에 endOffset을 저장할 records 배치를 만든다. 사건 키로 재시도 중복을 식별한다.</summary>
        internal UploadBatch(string cursorPath, long endOffset, JArray records)
        {
            CursorPath = cursorPath;
            EndOffset = endOffset;
            Records = records;
            BatchId = (string)records[0]["sessionId"] + ":" + (long)records[0]["eventSequence"]
                + ":" + (long)records[records.Count - 1]["eventSequence"];
        }
    }
}
