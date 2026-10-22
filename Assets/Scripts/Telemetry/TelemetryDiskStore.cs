using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string PROCESS_MARKER_FILE_NAME = "active-process.json";
        private readonly object _gate = new object();
        private readonly HashSet<string> _activeSessions = new HashSet<string>();
        private readonly string _root;
        private readonly long _maximumBytes;
        private long _writtenBytes;
        private bool _initialized;
        private string _readError;
        private string _metadataError;
        private string _processActiveSessionId;

        internal string Root => _root;
        internal string ReadError => _readError;
        internal string MetadataError => _metadataError;

        /// <summary>root와 maximumBytes를 보관한다. 실제 디렉터리 접근은 백그라운드 작업에서 수행한다.</summary>
        internal TelemetryDiskStore(string root, long maximumBytes)
        {
            _root = root;
            _maximumBytes = maximumBytes;
        }

        /// <summary>이전 process marker와 종료 사건이 없는 지원 스키마 로그를 검사해 unclean_exit를 추가한다. 실패한 원본은 보존한다.</summary>
        internal Task<string> RecoverInterruptedSessionsAsync()
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    Initialize();
                    List<string> errors = new List<string>();
                    RecoverProcessMarkerLocked(errors);
                    foreach (string path in Directory.GetFiles(_root, "*.jsonl"))
                    {
                        string sessionId = Path.GetFileNameWithoutExtension(path);
                        if (!Guid.TryParseExact(sessionId, "N", out _)) continue;
                        try
                        {
                            JObject last = ReadLastRecordAndRepairTail(path);
                            if (last == null) continue;
                            string schema = (string)last["schemaVersion"];
                            if (!new[] { "0.3.0", "0.4.0", TelemetryJson.SCHEMA_VERSION }.Contains(schema)
                                || (string)last["sessionId"] != sessionId)
                                continue;
                            if ((string)last["eventType"] == "session_ended") continue;

                            _activeSessions.Add(sessionId);
                            try
                            {
                                AppendRecordsLocked(new[] { CreateRecoveryRecord(last).ToString(Formatting.None) });
                            }
                            catch (Exception exception)
                            {
                                errors.Add("이전 세션 종료 복구 실패: " + sessionId + " / " + exception.Message);
                            }
                        }
                        catch (Exception exception) when (exception is IOException || exception is JsonException
                            || exception is UnauthorizedAccessException || exception is FormatException)
                        {
                            _activeSessions.Add(sessionId);
                            errors.Add("이전 세션 원본 보존: " + Path.GetFileName(path) + " / " + exception.Message);
                        }
                    }
                    return string.Join(" | ", errors);
                }
            });
        }

        /// <summary>records를 세션별 JSONL에 추가하고 최초 메타데이터를 JSON으로 저장한다. 실패는 Task 오류로 반환한다.</summary>
        internal Task AppendAsync(string[] records)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    Initialize();
                    AppendRecordsLocked(records);
                }
            });
        }

        /// <summary>sessionId별 records를 원본에 내구성 있게 추가하고 process marker가 없는 종료 세션을 활성 집합에서 제거한다. 보조 메타데이터 실패는 원본 저장을 실패 처리하지 않는다.</summary>
        private void AppendRecordsLocked(string[] records)
        {
            foreach (IGrouping<string, string> group in records.GroupBy(line => (string)JObject.Parse(line)["sessionId"]))
            {
                _activeSessions.Add(group.Key);
                string content = string.Join("\n", group) + "\n";
                string path = Path.Combine(_root, group.Key + ".jsonl");
                if (File.Exists(path)) ReadLastRecordAndRepairTail(path);
                long bytes = Encoding.UTF8.GetByteCount(content);
                if (_writtenBytes + bytes > _maximumBytes)
                    throw new IOException("현재 스키마의 로컬 로그 용량 한도에 도달했습니다. 원본을 백업한 뒤 공간을 확보하세요.");
                using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    byte[] data = Encoding.UTF8.GetBytes(content);
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                _writtenBytes += bytes;
                foreach (string line in group)
                {
                    try { UpdateProcessMarkerLocked(group.Key, JObject.Parse(line)); }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        _metadataError = "강제 종료 복구 표시 저장 실패: " + exception.Message;
                    }
                }
                string metadataPath = Path.Combine(_root, group.Key + ".json");
                if (!File.Exists(metadataPath))
                {
                    try
                    {
                        JObject first = JObject.Parse(group.First());
                        File.WriteAllText(metadataPath, first.ToString(Formatting.Indented), new UTF8Encoding(false));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        _metadataError = "로그 메타데이터 저장 실패: " + exception.Message;
                    }
                }
                if ((string)JObject.Parse(group.Last())["eventType"] == "session_ended"
                    && _processActiveSessionId != group.Key)
                    _activeSessions.Remove(group.Key);
            }
        }

        /// <summary>marker가 가리키는 마지막 도전을 다음 실행에서 unclean_exit로 복구한다. 실제 종료 시각은 만들지 않는다.</summary>
        private void RecoverProcessMarkerLocked(List<string> errors)
        {
            string markerPath = Path.Combine(_root, PROCESS_MARKER_FILE_NAME);
            if (!File.Exists(markerPath)) return;
            try
            {
                JObject marker = JObject.Parse(File.ReadAllText(markerPath, Encoding.UTF8));
                string sessionId = marker.Value<string>("sessionId");
                if (!Guid.TryParseExact(sessionId, "N", out _))
                {
                    errors.Add("강제 종료 복구 표시의 세션 ID가 올바르지 않아 원본 표시를 보존했습니다.");
                    return;
                }
                _processActiveSessionId = sessionId;
                _activeSessions.Add(sessionId);
                string sessionPath = Path.Combine(_root, sessionId + ".jsonl");
                if (!File.Exists(sessionPath))
                {
                    errors.Add("강제 종료 복구 표시가 있지만 연결된 JSONL 원본이 없습니다: "+sessionId);
                    return;
                }
                JObject last = ReadLastRecordAndRepairTail(sessionPath);
                if (last == null || (string)last["sessionId"] != sessionId) return;
                string reason = (string)last["payload"]?["reason"];
                if ((string)last["eventType"] == "session_ended" && (reason == "application_quit" || reason == "unclean_exit"))
                {
                    File.Delete(markerPath);
                    _processActiveSessionId = null;
                    _activeSessions.Remove(sessionId);
                    return;
                }
                string schema = (string)last["schemaVersion"];
                if (!new[] { "0.3.0", "0.4.0", TelemetryJson.SCHEMA_VERSION }.Contains(schema)) return;
                try { AppendRecordsLocked(new[] { CreateRecoveryRecord(last).ToString(Formatting.None) }); }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                    || exception is JsonException || exception is FormatException)
                { errors.Add("강제 종료 복구 사건 저장 실패: " + sessionId + " / " + exception.Message); }
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException
                || exception is UnauthorizedAccessException || exception is FormatException)
            { errors.Add("강제 종료 복구 표시를 읽지 못해 원본을 보존했습니다: " + exception.Message); }
        }

        /// <summary>durable stage_started에서 복구 표시를 만들고 정상 종료 또는 복구 종료가 저장되면 표시를 지운다.</summary>
        private void UpdateProcessMarkerLocked(string sessionId,JObject record)
        {
            string eventType = (string)record["eventType"], markerPath = Path.Combine(_root, PROCESS_MARKER_FILE_NAME);
            if (eventType == "stage_started")
            {
                string temporary = markerPath + ".tmp";
                byte[] content = Encoding.UTF8.GetBytes(record.ToString(Formatting.None));
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(content, 0, content.Length); stream.Flush(true); }
                if (File.Exists(markerPath)) File.Replace(temporary, markerPath, null);
                else File.Move(temporary, markerPath);
                _processActiveSessionId = sessionId;
                _activeSessions.Add(sessionId);
                return;
            }
            if (eventType != "session_ended") return;
            string reason = (string)record["payload"]?["reason"];
            if ((reason == "application_quit" || reason == "unclean_exit") && _processActiveSessionId == sessionId)
            {
                if (File.Exists(markerPath)) File.Delete(markerPath);
                _processActiveSessionId = null;
                _activeSessions.Remove(sessionId);
            }
        }

        /// <summary>path의 마지막 완전한 JSONL 사건을 읽고 잘린 끝부분만 제거한다. 파일 끝 사건을 반환한다.</summary>
        private JObject ReadLastRecordAndRepairTail(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                long originalLength = stream.Length;
                if (originalLength == 0) return null;
                stream.Position = originalLength - 1;
                if (stream.ReadByte() != '\n')
                {
                    long newline = FindPreviousNewline(stream, originalLength - 1);
                    long repairedLength = newline + 1;
                    stream.SetLength(repairedLength);
                    _writtenBytes = Directory.GetFiles(_root, "*.jsonl").Sum(GetStorageBudgetBytes);
                    if (repairedLength == 0) return null;
                }

                long lineEnd = stream.Length - 1;
                long previousNewline = FindPreviousNewline(stream, lineEnd - 1);
                long lineStart = previousNewline + 1;
                int lineLength = checked((int)(lineEnd - lineStart));
                if (lineLength <= 0) return null;
                byte[] line = new byte[lineLength];
                stream.Position = lineStart;
                int read = 0;
                while (read < line.Length)
                {
                    int count = stream.Read(line, read, line.Length - read);
                    if (count <= 0) throw new EndOfStreamException("마지막 JSONL 사건을 끝까지 읽지 못했습니다.");
                    read += count;
                }
                return JObject.Parse(Encoding.UTF8.GetString(line));
            }
        }

        /// <summary>stream에서 startAt 이전의 가장 가까운 줄바꿈 위치를 반환한다. 없으면 -1을 반환한다.</summary>
        private static long FindPreviousNewline(FileStream stream, long startAt)
        {
            for (long index = startAt; index >= 0; index--)
            {
                stream.Position = index;
                if (stream.ReadByte() == '\n') return index;
            }
            return -1;
        }

        /// <summary>last의 세션 식별과 순번을 이어받아 복구 감지 시각의 unclean_exit 사건을 반환한다.</summary>
        private static JObject CreateRecoveryRecord(JObject last)
        {
            JObject recovered = (JObject)last.DeepClone();
            long sequence = (long)last["eventSequence"] + 1;
            string now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9))
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            recovered["eventSequence"] = sequence;
            recovered["sourceId"] = "telemetry.recovery";
            recovered["sourceSequence"] = sequence;
            recovered["eventType"] = "session_ended";
            recovered["occurredAtKst"] = now;
            recovered["recordedAtKst"] = now;
            recovered["correlation"] = new JObject { ["inputId"] = 0, ["actionId"] = 0,
                ["attackId"] = 0, ["hitId"] = 0, ["projectileId"] = 0, ["dodgeId"] = 0 };
            recovered["payload"] = new JObject { ["reason"] = "unclean_exit",
                ["exitStageId"] = (string)last["stageId"],
                ["exitStageAttemptId"] = (string)last["stageAttemptId"],
                ["exitLocation"] = (string)last["stageId"] };
            return recovered;
        }

        /// <summary>destination에서 전송 완료된 비활성 세션을 정리하고 커서 뒤의 완전한 줄을 limit·byteLimit 안에서 읽어 배치를 반환한다. 미확인 원본은 보존한다.</summary>
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
                            string cursorPath = path + "." + destination + ".cursor";
                            if (DeleteAcknowledgedSession(path, cursorPath)) continue;
                            // 이전 빌드의 세션은 새 수신기에 재전송하지 않는다.
                            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
                            {
                                string first = reader.ReadLine();
                                if (string.IsNullOrWhiteSpace(first))
                                    continue;
                                string schema = (string)JObject.Parse(first)["schemaVersion"];
                                if (!new[] { "0.3.0", "0.4.0", TelemetryJson.SCHEMA_VERSION }.Contains(schema))
                                    continue;
                            }
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

        /// <summary>서버가 확인한 batch의 끝 위치를 원자적으로 저장하고 종료된 세션 전체가 ACK되면 로컬 파일을 삭제한다. 미확인 사건은 재전송 대상으로 남긴다.</summary>
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
                    string sessionId = (string)batch.Records[0]["sessionId"];
                    DeleteAcknowledgedSession(Path.Combine(_root, sessionId + ".jsonl"), path);
                }
            });
        }

        /// <summary>path가 종료된 세션이고 cursorPath가 파일 전체의 ACK를 나타내면 원본과 부속 파일을 삭제한다. 삭제한 용량을 예산에서 차감하고 성공 여부를 반환한다.</summary>
        private bool DeleteAcknowledgedSession(string path, string cursorPath)
        {
            string sessionId = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(sessionId, "N", out _) || _activeSessions.Contains(sessionId)
                || !File.Exists(path) || !File.Exists(cursorPath)) return false;
            long length = new FileInfo(path).Length;
            if (length == 0 || !long.TryParse(File.ReadAllText(cursorPath), out long offset) || offset != length)
                return false;

            long budgetBytes = GetStorageBudgetBytes(path);
            File.Delete(path);
            _writtenBytes = Math.Max(0L, _writtenBytes - budgetBytes);
            File.Delete(Path.Combine(_root, sessionId + ".json"));
            foreach (string cursor in Directory.GetFiles(_root, sessionId + ".jsonl.*.cursor"))
                File.Delete(cursor);
            return true;
        }

        /// <summary>전용 폴더를 준비하고 현재 스키마의 JSONL 용량을 집계한다. 구버전 원본을 보존하며 _writtenBytes를 초기화한다. 호출자는 _gate를 소유한다.</summary>
        private void Initialize()
        {
            if (_initialized) return;
            Directory.CreateDirectory(_root);
            _writtenBytes = Directory.GetFiles(_root, "*.jsonl").Sum(GetStorageBudgetBytes);
            _initialized = true;
        }

        /// <summary>path의 첫 사건에서 스키마를 읽어 현재 저장 한도에 포함할 바이트를 반환한다. 다른 버전은 제외하고 판독 불가 파일은 용량에 포함한다.</summary>
        private long GetStorageBudgetBytes(string path)
        {
            long bytes = new FileInfo(path).Length;
            try
            {
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
                {
                    string first = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(first)) return bytes;
                    string schema = (string)JObject.Parse(first)["schemaVersion"];
                    if (!string.IsNullOrEmpty(schema)
                        && !new[] { "0.3.0", "0.4.0", TelemetryJson.SCHEMA_VERSION }.Contains(schema)) return 0L;
                }
            }
            catch (JsonException) { return bytes; }
            return bytes;
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
