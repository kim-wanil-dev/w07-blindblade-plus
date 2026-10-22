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
{internal sealed class TelemetryDiskStore
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

	/// <summary>
	/// JSONL 로그 저장소를 초기화한다.
	/// root와 maximumBytes를 사용하며 저장 위치와 용량 한도를 설정한다.
	/// </summary>
	internal TelemetryDiskStore(string root, long maximumBytes)
	{
		_root = root;
		_maximumBytes = maximumBytes;
	}

	/// <summary>
	/// 이전 실행의 미완료 세션을 복구하고 복구 결과를 비동기로 반환한다.
	/// 저장된 프로세스 마커와 JSONL 파일을 사용하며 복구 이벤트를 기록한다.
	/// </summary>
	internal Task<string> RecoverInterruptedSessionsAsync()
	{
		return Task.Run(delegate
		{
			lock (_gate)
			{
				Initialize();
				List<string> list = new List<string>();
				RecoverProcessMarkerLocked(list);
				string[] files = Directory.GetFiles(_root, "*.jsonl");
				foreach (string path in files)
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
					if (Guid.TryParseExact(fileNameWithoutExtension, "N", out var _))
					{
						try
						{
							JObject val = ReadLastRecordAndRepairTail(path);
							if (val != null)
							{
								string value = (string)val["schemaVersion"];
								if (new string[3] { "0.3.0", "0.4.0", "0.5.0" }.Contains(value) && !((string)val["sessionId"] != fileNameWithoutExtension) && !((string)val["eventType"] == "session_ended"))
								{
									_activeSessions.Add(fileNameWithoutExtension);
									try
									{
										AppendRecordsLocked(new string[1] { ((JToken)CreateRecoveryRecord(val)).ToString((Formatting)0, Array.Empty<JsonConverter>()) });
									}
									catch (Exception ex)
									{
										list.Add("이전 세션 종료 복구 실패: " + fileNameWithoutExtension + " / " + ex.Message);
									}
								}
							}
						}
						catch (Exception ex2) when (ex2 is IOException || ex2 is JsonException || ex2 is UnauthorizedAccessException || ex2 is FormatException)
						{
							_activeSessions.Add(fileNameWithoutExtension);
							list.Add("이전 세션 원본 보존: " + Path.GetFileName(path) + " / " + ex2.Message);
						}
					}
				}
				return string.Join(" | ", list);
			}
		});
	}

	/// <summary>
	/// 이벤트 줄을 JSONL 원본과 메타데이터에 비동기로 추가한다.
	/// records를 사용하며 파일, 프로세스 마커, 저장 용량 상태를 변경한다.
	/// </summary>
	internal Task AppendAsync(string[] records)
	{
		return Task.Run(delegate
		{
			lock (_gate)
			{
				Initialize();
				AppendRecordsLocked(records);
			}
		});
	}

	/// <summary>
	/// 잠금 상태에서 세션별 이벤트 줄과 메타데이터를 디스크에 추가한다.
	/// records를 사용하며 원본 파일과 프로세스 상태를 변경한다.
	/// </summary>
	private void AppendRecordsLocked(string[] records)
	{
		foreach (IGrouping<string, string> item in from line in records
			group line by (string)JObject.Parse(line)["sessionId"])
		{
			_activeSessions.Add(item.Key);
			string s = string.Join("\n", item) + "\n";
			string path = Path.Combine(_root, item.Key + ".jsonl");
			if (File.Exists(path))
			{
				ReadLastRecordAndRepairTail(path);
			}
			long num = Encoding.UTF8.GetByteCount(s);
			if (_writtenBytes + num > _maximumBytes)
			{
				throw new IOException("현재 스키마의 로컬 로그 용량 한도에 도달했습니다. 원본을 백업한 뒤 공간을 확보하세요.");
			}
			using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
			{
				byte[] bytes = Encoding.UTF8.GetBytes(s);
				fileStream.Write(bytes, 0, bytes.Length);
				fileStream.Flush(flushToDisk: true);
			}
			_writtenBytes += num;
			foreach (string item2 in item)
			{
				try
				{
					UpdateProcessMarkerLocked(item.Key, JObject.Parse(item2));
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					_metadataError = "강제 종료 복구 표시 저장 실패: " + ex.Message;
				}
			}
			string path2 = Path.Combine(_root, item.Key + ".json");
			if (!File.Exists(path2))
			{
				try
				{
					JObject val = JObject.Parse(item.First());
					File.WriteAllText(path2, ((JToken)val).ToString((Formatting)1, Array.Empty<JsonConverter>()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				}
				catch (Exception ex2) when (ex2 is IOException || ex2 is UnauthorizedAccessException)
				{
					_metadataError = "로그 메타데이터 저장 실패: " + ex2.Message;
				}
			}
			if ((string)JObject.Parse(item.Last())["eventType"] == "session_ended" && _processActiveSessionId != item.Key)
			{
				_activeSessions.Remove(item.Key);
			}
		}
	}

	/// <summary>
	/// 비정상 종료 마커의 세션 기록을 복구하고 오류를 수집한다.
	/// errors 목록을 사용하며 복구된 세션과 저장소 상태를 변경한다.
	/// </summary>
	private void RecoverProcessMarkerLocked(List<string> errors)
	{
		string path = Path.Combine(_root, "active-process.json");
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			JObject val = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
			string text = ((JToken)val).Value<string>((object)"sessionId");
			if (!Guid.TryParseExact(text, "N", out var _))
			{
				errors.Add("강제 종료 복구 표시의 세션 ID가 올바르지 않아 원본 표시를 보존했습니다.");
				return;
			}
			_processActiveSessionId = text;
			_activeSessions.Add(text);
			string path2 = Path.Combine(_root, text + ".jsonl");
			if (!File.Exists(path2))
			{
				errors.Add("강제 종료 복구 표시가 있지만 연결된 JSONL 원본이 없습니다: " + text);
				return;
			}
			JObject val2 = ReadLastRecordAndRepairTail(path2);
			if (val2 == null || (string)val2["sessionId"] != text)
			{
				return;
			}
			JToken obj = val2["payload"];
			string text2 = (string)((obj != null) ? obj[(object)"reason"] : null);
			if ((string)val2["eventType"] == "session_ended" && (text2 == "application_quit" || text2 == "unclean_exit"))
			{
				File.Delete(path);
				_processActiveSessionId = null;
				_activeSessions.Remove(text);
				return;
			}
			string value = (string)val2["schemaVersion"];
			if (!new string[3] { "0.3.0", "0.4.0", "0.5.0" }.Contains(value))
			{
				return;
			}
			try
			{
				AppendRecordsLocked(new string[1] { ((JToken)CreateRecoveryRecord(val2)).ToString((Formatting)0, Array.Empty<JsonConverter>()) });
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is FormatException)
			{
				errors.Add("강제 종료 복구 사건 저장 실패: " + text + " / " + ex.Message);
			}
		}
		catch (Exception ex2) when (ex2 is IOException || ex2 is JsonException || ex2 is UnauthorizedAccessException || ex2 is FormatException)
		{
			errors.Add("강제 종료 복구 표시를 읽지 못해 원본을 보존했습니다: " + ex2.Message);
		}
	}

	/// <summary>
	/// 현재 프로세스가 기록 중인 세션의 복구 마커를 갱신한다.
	/// sessionId와 record를 사용하며 활성 세션 메타데이터를 변경한다.
	/// </summary>
	private void UpdateProcessMarkerLocked(string sessionId, JObject record)
	{
		string text = (string)record["eventType"];
		string text2 = Path.Combine(_root, "active-process.json");
		if (text == "stage_started")
		{
			string text3 = text2 + ".tmp";
			byte[] bytes = Encoding.UTF8.GetBytes(((JToken)record).ToString((Formatting)0, Array.Empty<JsonConverter>()));
			using (FileStream fileStream = new FileStream(text3, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(bytes, 0, bytes.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(text2))
			{
				File.Replace(text3, text2, null);
			}
			else
			{
				File.Move(text3, text2);
			}
			_processActiveSessionId = sessionId;
			_activeSessions.Add(sessionId);
		}
		else
		{
			if (text != "session_ended")
			{
				return;
			}
			JToken obj = record["payload"];
			string text4 = (string)((obj != null) ? obj[(object)"reason"] : null);
			if ((text4 == "application_quit" || text4 == "unclean_exit") && _processActiveSessionId == sessionId)
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
				_processActiveSessionId = null;
				_activeSessions.Remove(sessionId);
			}
		}
	}

	/// <summary>
	/// JSONL 마지막 레코드를 읽고 잘린 끝부분을 복구한다.
	/// path를 사용하고 복구된 마지막 이벤트 객체를 반환한다.
	/// </summary>
	private JObject ReadLastRecordAndRepairTail(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
		long length = fileStream.Length;
		if (length == 0)
		{
			return null;
		}
		fileStream.Position = length - 1;
		if (fileStream.ReadByte() != 10)
		{
			long num = FindPreviousNewline(fileStream, length - 1);
			long num2 = num + 1;
			fileStream.SetLength(num2);
			_writtenBytes = ((IEnumerable<string>)Directory.GetFiles(_root, "*.jsonl")).Sum((Func<string, long>)GetStorageBudgetBytes);
			if (num2 == 0)
			{
				return null;
			}
		}
		long num3 = fileStream.Length - 1;
		long num4 = FindPreviousNewline(fileStream, num3 - 1);
		long num5 = num4 + 1;
		int num6 = checked((int)(num3 - num5));
		if (num6 <= 0)
		{
			return null;
		}
		byte[] array = new byte[num6];
		fileStream.Position = num5;
		int num7;
		for (int i = 0; i < array.Length; i += num7)
		{
			num7 = fileStream.Read(array, i, array.Length - i);
			if (num7 <= 0)
			{
				throw new EndOfStreamException("마지막 JSONL 사건을 끝까지 읽지 못했습니다.");
			}
		}
		return JObject.Parse(Encoding.UTF8.GetString(array));
	}

	/// <summary>
	/// 스트림에서 시작 위치 이전의 가장 가까운 줄바꿈 오프셋을 찾는다.
	/// stream과 startAt을 사용하고 오프셋을 반환한다.
	/// </summary>
	private static long FindPreviousNewline(FileStream stream, long startAt)
	{
		for (long num = startAt; num >= 0; num--)
		{
			stream.Position = num;
			if (stream.ReadByte() == 10)
			{
				return num;
			}
		}
		return -1L;
	}

	/// <summary>
	/// 마지막 세션 이벤트를 바탕으로 비정상 종료 복구 이벤트를 만든다.
	/// last 레코드를 사용하고 새 복구 이벤트 객체를 반환한다.
	/// </summary>
	private static JObject CreateRecoveryRecord(JObject last)
	{
		JObject val = (JObject)((JToken)last).DeepClone();
		long num = (long)last["eventSequence"] + 1;
		string text = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9.0)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
		val["eventSequence"] = num;
		val["sourceId"] = "telemetry.recovery";
		val["sourceSequence"] = num;
		val["eventType"] = "session_ended";
		val["occurredAtKst"] = text;
		val["recordedAtKst"] = text;
		val["correlation"] = (JToken)new JObject
		{
			["inputId"] = 0,
			["actionId"] = 0,
			["attackId"] = 0,
			["hitId"] = 0,
			["projectileId"] = 0,
			["dodgeId"] = 0
		};
		val["payload"] = (JToken)new JObject
		{
			["reason"] = "unclean_exit",
			["exitStageId"] = (string)last["stageId"],
			["exitStageAttemptId"] = (string)last["stageAttemptId"],
			["exitLocation"] = (string)last["stageId"]
		};
		return val;
	}

	/// <summary>
	/// 대상 세션의 미확인 JSONL 레코드를 배치로 읽는다.
	/// limit, byteLimit, destination, includedSessionIds를 사용하고 UploadBatch를 반환한다.
	/// </summary>
	internal Task<UploadBatch> ReadBatchAsync(int limit, int byteLimit, string destination, HashSet<string> includedSessionIds = null)
	{
		return Task.Run(delegate
		{
			lock (_gate)
			{
				Initialize();
				_readError = null;
				foreach (string item in Directory.GetFiles(_root, "*.jsonl").OrderBy(File.GetLastWriteTimeUtc))
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item);
					if (includedSessionIds == null || includedSessionIds.Contains(fileNameWithoutExtension))
					{
						try
						{
							string text = item + "." + destination + ".cursor";
							if (!DeleteAcknowledgedSession(item, text))
							{
								using StreamReader streamReader = new StreamReader(item, Encoding.UTF8);
								string text2 = streamReader.ReadLine();
								if (!string.IsNullOrWhiteSpace(text2))
								{
									string value = (string)JObject.Parse(text2)["schemaVersion"];
									if (new string[3] { "0.3.0", "0.4.0", "0.5.0" }.Contains(value))
									{
										goto IL_0150;
									}
								}
							}
							goto end_IL_009a;
							IL_0150:
							long num = (File.Exists(text) ? long.Parse(File.ReadAllText(text)) : 0);
							JArray val = new JArray();
							long endOffset = num;
							using (FileStream fileStream = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
							{
								if (num < 0 || num > fileStream.Length)
								{
									throw new IOException("전송 커서가 원본 범위를 벗어났습니다: " + Path.GetFileName(item));
								}
								fileStream.Position = num;
								using MemoryStream memoryStream = new MemoryStream();
								while (fileStream.Position < fileStream.Length && ((JContainer)val).Count < limit)
								{
									int num2 = fileStream.ReadByte();
									if (num2 != 10)
									{
										memoryStream.WriteByte((byte)num2);
										if (memoryStream.Length > byteLimit)
										{
											throw new IOException("JSONL 한 줄이 전송 크기 한도를 넘었습니다: " + Path.GetFileName(item));
										}
									}
									else
									{
										if (fileStream.Position - num > byteLimit && ((JContainer)val).Count > 0)
										{
											break;
										}
										try
										{
											val.Add((JToken)(object)JObject.Parse(Encoding.UTF8.GetString(memoryStream.ToArray())));
										}
										catch (JsonException)
										{
											if (((JContainer)val).Count > 0)
											{
												break;
											}
											throw;
										}
										memoryStream.SetLength(0L);
										endOffset = fileStream.Position;
									}
								}
								if (memoryStream.Length > 0 && ((JContainer)val).Count == 0)
								{
									throw new IOException("불완전한 JSONL 끝줄이 있습니다. 전처리 보고서로 확인하세요: " + Path.GetFileName(item));
								}
							}
							if (((JContainer)val).Count > 0)
							{
								return new UploadBatch(text, endOffset, val);
							}
							end_IL_009a:;
						}
						catch (Exception ex2) when (ex2 is IOException || ex2 is JsonException || ex2 is FormatException)
						{
							_readError = "원본 전송 보류: " + Path.GetFileName(item) + " / " + ex2.Message;
						}
					}
				}
				return (UploadBatch)null;
			}
		});
	}

	/// <summary>
	/// 서버가 확인한 배치의 ACK 커서를 저장한다.
	/// batch를 사용하며 전송 커서와 완료 세션 파일 상태를 변경한다.
	/// </summary>
	internal Task AcknowledgeAsync(UploadBatch batch)
	{
		return Task.Run(delegate
		{
			lock (_gate)
			{
				string cursorPath = batch.CursorPath;
				string text = cursorPath + ".tmp";
				File.WriteAllText(text, batch.EndOffset.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				if (File.Exists(cursorPath))
				{
					File.Replace(text, cursorPath, null);
				}
				else
				{
					File.Move(text, cursorPath);
				}
				string text2 = (string)batch.Records[0][(object)"sessionId"];
				DeleteAcknowledgedSession(Path.Combine(_root, text2 + ".jsonl"), cursorPath);
			}
		});
	}

	/// <summary>
	/// 전체 ACK된 비활성 세션의 JSONL과 보조 파일을 정리한다.
	/// path와 cursorPath를 사용하고 파일 정리 여부를 반환한다.
	/// </summary>
	private bool DeleteAcknowledgedSession(string path, string cursorPath)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		if (!Guid.TryParseExact(fileNameWithoutExtension, "N", out var _) || _activeSessions.Contains(fileNameWithoutExtension) || !File.Exists(path) || !File.Exists(cursorPath))
		{
			return false;
		}
		long length = new FileInfo(path).Length;
		if (length == 0L || !long.TryParse(File.ReadAllText(cursorPath), out var result2) || result2 != length)
		{
			return false;
		}
		long storageBudgetBytes = GetStorageBudgetBytes(path);
		File.Delete(path);
		_writtenBytes = Math.Max(0L, _writtenBytes - storageBudgetBytes);
		File.Delete(Path.Combine(_root, fileNameWithoutExtension + ".json"));
		string[] files = Directory.GetFiles(_root, fileNameWithoutExtension + ".jsonl.*.cursor");
		foreach (string path2 in files)
		{
			File.Delete(path2);
		}
		return true;
	}

	/// <summary>
	/// 저장소 디렉터리를 준비하고 현재 스키마 로그 사용량을 계산한다.
	/// 저장소 필드를 사용하며 초기화 상태와 용량 집계를 변경한다.
	/// </summary>
	private void Initialize()
	{
		if (!_initialized)
		{
			Directory.CreateDirectory(_root);
			_writtenBytes = ((IEnumerable<string>)Directory.GetFiles(_root, "*.jsonl")).Sum((Func<string, long>)GetStorageBudgetBytes);
			_initialized = true;
		}
	}

	/// <summary>
	/// 로그 파일의 스키마를 확인해 저장 용량에 반영할 크기를 계산한다.
	/// path를 사용하고 용량 계산값을 반환한다.
	/// </summary>
	private long GetStorageBudgetBytes(string path)
	{
		long length = new FileInfo(path).Length;
		try
		{
			using StreamReader streamReader = new StreamReader(path, Encoding.UTF8);
			string text = streamReader.ReadLine();
			if (string.IsNullOrWhiteSpace(text))
			{
				return length;
			}
			string value = (string)JObject.Parse(text)["schemaVersion"];
			if (!string.IsNullOrEmpty(value) && !new string[3] { "0.3.0", "0.4.0", "0.5.0" }.Contains(value))
			{
				return 0L;
			}
		}
		catch (JsonException)
		{
			return length;
		}
		return length;
	}
}

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
