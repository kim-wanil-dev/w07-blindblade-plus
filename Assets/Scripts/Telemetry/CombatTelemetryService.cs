using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
public sealed class CombatTelemetryService : MonoBehaviour
{
	private const int MAXIMUM_BATCH_BYTES = 196608;

	private const double RETRY_INTERVAL_SECONDS = 20.0;

	private const double QUIT_UPLOAD_TIMEOUT_SECONDS = 8.0;

	private const double BACKGROUND_START_TIMEOUT_SECONDS = 3.0;

	private static CombatTelemetryService _active;

	private readonly List<string> _buffer = new List<string>();

	private readonly HashSet<string> _pendingUploadSessionIds = new HashSet<string>();

	private readonly Dictionary<string, JObject> _priorityRecords = new Dictionary<string, JObject>();

	private readonly HashSet<string> _priorityAcknowledgedKeys = new HashSet<string>();

	private readonly Dictionary<string, long> _contiguousAcknowledgedSequences = new Dictionary<string, long>();

	private TelemetryDiskStore _store;

	private Task<string> _startupRecovery;

	private Task _write;

	private string[] _writing;

	private UnityWebRequest _request;

	private int _bufferLimit;

	private int _batchLimit;

	private int _timeout;

	private float _flushInterval;

	private float _uploadInterval;

	private double _nextFlush;

	private double _nextUpload;

	private double _retryNotBefore;

	private double _writeRetryNotBefore;

	private long _recordVersion;

	private long _writingRecordVersion;

	private long _persistedRecordVersion;

	private long _immediateRequestVersion;

	private bool _flushRequested;

	private bool _immediateUploadRequested;

	private bool _diskDrained;

	private bool _uploadDisabled;

	private bool _quitAllowed;

	private bool _allowUpload;

	private bool _uploading;

	private bool _quitting;

	private bool _quitRequested;

	private bool _quitNotified;


	private string _lastError;

	private string _lastWarnedError;

	private JObject _latestAttemptRecord;

	private double _latestAttemptObservedRealtime;

	private int _dropped;

	private int _uploaded;

	private string _anonymousPlayerId;

	private string _configurationSource;

	public static CombatTelemetryService Active => _active;

	public string LogDirectory => _store?.Root;

	public string LastError => _lastError;

	public int DroppedEvents => _dropped;

	public int AcknowledgedEvents => _uploaded;

	public int BufferedEvents
	{
		get
		{
			int count = _buffer.Count;
			string[] writing = _writing;
			return count + ((writing != null) ? writing.Length : 0);
		}
	}

	public string AnonymousPlayerId => _anonymousPlayerId;

	public string ConfigurationSource => _configurationSource;

	public event Action QuitRequested;

	/// <summary>
	/// Telemetry 싱글턴을 생성하거나 기존 인스턴스를 반환한다.
	/// 설정값을 사용해 저장소와 전송 주기를 초기화하고 Active 상태를 반환한다.
	/// </summary>
	public static CombatTelemetryService GetOrCreate(int bufferLimit, float flushInterval, int maximumMegabytes, bool allowUpload, float uploadInterval, int batchLimit, int timeout)
	{
		if ((UnityEngine.Object)(object)_active != (UnityEngine.Object)null)
		{
			return _active;
		}
		GameObject host = new GameObject("CombatTelemetryService");
		UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)(object)host);
		_active = host.AddComponent<CombatTelemetryService>();
		_active._store = new TelemetryDiskStore(Path.Combine(Application.persistentDataPath, "CombatTelemetry"), (long)Math.Max(1, maximumMegabytes) * 1024L * 1024);
		_active._startupRecovery = _active._store.RecoverInterruptedSessionsAsync();
		_active._bufferLimit = Math.Max(64, bufferLimit);
		_active._flushInterval = Mathf.Max(0.2f, flushInterval);
		_active._uploadInterval = Mathf.Max(5f, uploadInterval);
		_active._batchLimit = Mathf.Clamp(batchLimit, 1, 100);
		_active._timeout = Mathf.Clamp(timeout, 5, 120);
		_active._allowUpload = allowUpload;
		if (!Application.isEditor)
		{
			Application.wantsToQuit += _active.HandleWantsToQuit;
		}
		#if UNITY_EDITOR
		EditorApplication.playModeStateChanged += _active.HandleEditorPlayModeChanged;
#endif
		_active._anonymousPlayerId = Guid.NewGuid().ToString("N");
		try
		{
			Directory.CreateDirectory(_active._store.Root);
			string identityPath = Path.Combine(_active._store.Root, "anonymous-player-id.txt");
			if (File.Exists(identityPath) && Guid.TryParseExact(File.ReadAllText(identityPath).Trim(), "N", out var identity))
			{
				_active._anonymousPlayerId = identity.ToString("N");
			}
			else
			{
				File.WriteAllText(identityPath, _active._anonymousPlayerId);
			}
		}
		catch (Exception ex)
		{
			_active.ReportError("익명 ID 저장 실패: " + ex.Message);
		}
		string resumeError = CombatTelemetryBackgroundUpload.ResumePending(_active._store.Root);
		if (!string.IsNullOrEmpty(resumeError)) _active.ReportError(resumeError);
		return _active;
	}

	/// <summary>
	/// 이벤트를 메모리 버퍼에 추가하고 해당 세션을 현재 실행의 전송 대상으로 등록한다.
	/// record를 사용하며 버퍼와 현재 세션 ID 집합을 변경한다.
	/// </summary>
	internal void Enqueue(JObject record)
	{
		if (_quitting)
		{
			return;
		}
		string eventType = ((JToken)record).Value<string>((object)"eventType");
		string sessionId = ((JToken)record).Value<string>((object)"sessionId");
		if (!string.IsNullOrEmpty(sessionId))
		{
			_pendingUploadSessionIds.Add(sessionId);
		}
		int num;
		switch (eventType)
		{
		default:
			num = ((eventType == "game_completed") ? 1 : 0);
			break;
		case "player_died":
		case "stage_started":
		case "stage_summary":
		case "stage_ended":
		case "session_ended":
			num = 1;
			break;
		}
		bool terminal = (byte)num != 0;
		if (BufferedEvents >= _bufferLimit && !terminal)
		{
			_dropped++;
			ReportError("로그 메모리 한도를 넘어 사건이 누락되었습니다.");
			return;
		}
		_buffer.Add(((JToken)record).ToString((Formatting)0, Array.Empty<JsonConverter>()));
		string attemptId = ((JToken)record).Value<string>((object)"stageAttemptId");
		JObject latestAttemptRecord = _latestAttemptRecord;
		string currentAttemptId = ((latestAttemptRecord != null) ? ((JToken)latestAttemptRecord).Value<string>((object)"stageAttemptId") : null);
		if (eventType == "stage_started")
		{
			goto IL_0146;
		}
		if (!string.IsNullOrEmpty(attemptId) && attemptId == currentAttemptId)
		{
			string text = ((JToken)record).Value<string>((object)"sessionId");
			JObject latestAttemptRecord2 = _latestAttemptRecord;
			if (text == ((latestAttemptRecord2 != null) ? ((JToken)latestAttemptRecord2).Value<string>((object)"sessionId") : null))
			{
				goto IL_0146;
			}
		}
		goto IL_0164;
		IL_0164:
		_recordVersion++;
		_diskDrained = false;
		return;
		IL_0146:
		_latestAttemptRecord = (JObject)((JToken)record).DeepClone();
		_latestAttemptObservedRealtime = Time.realtimeSinceStartupAsDouble;
		goto IL_0164;
	}

	/// <summary>
	/// 버퍼를 즉시 저장하고 정기 업로드 주기를 앞당긴다.
	/// 업로드가 실패한 경우에는 설정된 재시도 대기시간을 따른다.
	/// </summary>
	public void RequestFlush()
	{
		_flushRequested = true;
		_diskDrained = false;
		_nextFlush = 0.0;
	}

	/// <summary>
	/// 저장과 업로드를 즉시 요청하고 실패 재시도 대기를 건너뛴다.
	/// 직전 업로드가 진행 중이면 완료 후 우선 업로드하도록 요청 상태를 유지한다.
	/// </summary>
	public void RequestFlushImmediately()
	{
		RequestFlush();
		_immediateUploadRequested = true;
		_immediateRequestVersion = _recordVersion;
		_retryNotBefore = 0.0;
		_writeRetryNotBefore = 0.0;
	}

	private void Update()
	{
		if (_startupRecovery != null)
		{
			if (!_startupRecovery.IsCompleted)
			{
				return;
			}
			if (_startupRecovery.IsFaulted)
			{
				ReportError("이전 세션 복구 실패: " + _startupRecovery.Exception.GetBaseException().Message);
			}
			else if (_startupRecovery.IsCanceled)
			{
				ReportError("이전 세션 복구 작업이 취소되었습니다. 원본 로그를 보존하고 전송을 계속합니다.");
			}
			else if (!string.IsNullOrEmpty(_startupRecovery.Result))
			{
				ReportError(_startupRecovery.Result);
			}
			_startupRecovery = null;
		}
		ObserveWrite();
		double now = Time.realtimeSinceStartupAsDouble;
		if (_allowUpload && !_uploading && (_priorityRecords.Count > 0 || _write == null)
			&& (_priorityRecords.Count > 0 || !_immediateUploadRequested || _persistedRecordVersion >= _immediateRequestVersion)
			&& (_immediateUploadRequested || now >= _retryNotBefore)
			&& (_flushRequested || _priorityRecords.Count > 0 || now >= _nextUpload))
		{
			if (_write == null && _buffer.Count == 0) _flushRequested = false;
			if (_persistedRecordVersion >= _immediateRequestVersion) _immediateUploadRequested = false;
			((MonoBehaviour)this).StartCoroutine(Upload());
		}
		if (_write == null && _buffer.Count > 0 && now >= _writeRetryNotBefore && (_flushRequested || now >= _nextFlush))
		{
			_writing = _buffer.ToArray();
			_buffer.Clear();
			_writingRecordVersion = _recordVersion;
			_write = _store.AppendAsync(_writing);
			_nextFlush = now + (double)_flushInterval;
		}
	}

	/// <summary>
	/// 비동기 로컬 기록 결과를 확인하고 실패 상태를 갱신한다.
	/// 쓰기 작업 상태를 사용하며 버퍼, 재시도 시각, 오류 상태를 변경한다.
	/// </summary>
	private void ObserveWrite()
	{
		if (_write == null || !_write.IsCompleted)
		{
			return;
		}
		if (_write.IsFaulted || _write.IsCanceled)
		{
			if (_writing != null && _writing.Length != 0)
			{
				_buffer.InsertRange(0, _writing);
				_flushRequested = true;
			}
			_writeRetryNotBefore = Time.realtimeSinceStartupAsDouble + 20.0;
			_diskDrained = false;
			ReportError("로컬 저장 실패(일부 저장 가능): " + (_write.Exception?.GetBaseException().Message ?? "cancelled") + "; 원본 저장을 20초 뒤 다시 시도합니다.");
		}
		else
		{
			_persistedRecordVersion = _writingRecordVersion;
			foreach (string line in _writing) TrackPriorityRecord(JObject.Parse(line));
			_writeRetryNotBefore = 0.0;
			if (!string.IsNullOrEmpty(_store.MetadataError))
			{
				ReportError(_store.MetadataError);
			}
		}
		_write = null;
		_writing = null;
	}

	/// <summary>
	/// 저장 완료된 구간 상태와 종료 사건을 우선 대기열에 넣는다.
	/// record를 사용하며 같은 도전의 오래된 진행 요약은 최신 요약으로 대체한다.
	/// </summary>
	private void TrackPriorityRecord(JObject record)
	{
		string type = record.Value<string>("eventType");
		if (type != "stage_started" && type != "stage_summary" && type != "stage_ended"
			&& type != "player_died" && type != "game_completed" && type != "session_ended") return;
		string sessionId = record.Value<string>("sessionId");
		long sequence = record.Value<long>("eventSequence");
		string eventKey = sessionId + ":" + sequence;
		if ((_contiguousAcknowledgedSequences.TryGetValue(sessionId, out long acknowledged) && sequence <= acknowledged)
			|| _priorityAcknowledgedKeys.Contains(eventKey)) return;
		if (type == "stage_summary" || type == "stage_ended")
		{
			foreach (string key in new List<string>(_priorityRecords.Keys))
			{
				JObject previous = _priorityRecords[key];
				if (previous.Value<string>("sessionId") == record.Value<string>("sessionId")
					&& previous.Value<string>("stageAttemptId") == record.Value<string>("stageAttemptId")
					&& previous.Value<string>("eventType") == "stage_summary") _priorityRecords.Remove(key);
			}
		}
		_priorityRecords[eventKey] = record;
	}

	/// <summary>
	/// 미확인 배치를 서버로 전송하고 응답 ACK를 확인한다.
	/// 저장소와 현재 설정을 사용하며 업로드 커서와 전송 상태를 변경한다.
	/// </summary>
	private IEnumerator Upload()
	{
		_uploading = true;
		_nextUpload = Time.realtimeSinceStartupAsDouble + (double)_uploadInterval;
		JObject configuration;
		try
		{
			configuration = ReadUploadConfiguration();
			_uploadDisabled = ((JToken)configuration).Value<bool?>((object)"enabled") != true;
			if (_uploadDisabled)
			{
				_uploading = false;
				yield break;
			}
			if (!Uri.TryCreate(((JToken)configuration).Value<string>((object)"endpoint"), UriKind.Absolute, out Uri endpoint) || endpoint.Scheme != Uri.UriSchemeHttps || (((JToken)configuration).Value<string>((object)"token")?.Length ?? 0) < 32)
			{
				throw new InvalidDataException("전송 설정의 HTTPS endpoint와 32자 이상 token을 설정하세요. 설정 출처: " + _configurationSource);
			}
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			FailUpload("전송 설정 오류: " + exception.Message);
			yield break;
		}
		string destination;
		using (SHA256 algorithm = SHA256.Create())
		{
			destination = BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(((JToken)configuration).Value<string>((object)"endpoint")))).Replace("-", "").Substring(0, 32);
		}
		long readVersion = _recordVersion;
		UploadBatch batch = UploadBatch.CreatePriority(_priorityRecords.Values, _batchLimit, MAXIMUM_BATCH_BYTES);
		bool priority = batch != null;
		if (!priority)
		{
			Task<UploadBatch> read = _store.ReadBatchAsync(_batchLimit, MAXIMUM_BATCH_BYTES, destination, new HashSet<string>(_pendingUploadSessionIds));
			while (!read.IsCompleted) yield return null;
			if (read.IsFaulted || read.IsCanceled)
			{
				FailUpload("미전송 로그 읽기 실패: " + (read.Exception?.GetBaseException().Message ?? "cancelled"));
				yield break;
			}
			if (!string.IsNullOrEmpty(_store.ReadError)) ReportError(_store.ReadError);
			batch = read.Result;
		}
		if (batch == null)
		{
			if (!string.IsNullOrEmpty(_store.ReadError))
			{
				FailUpload(_store.ReadError);
				yield break;
			}
			_diskDrained = readVersion == _recordVersion && _write == null && _buffer.Count == 0 && _priorityRecords.Count == 0;
			_uploading = false;
			yield break;
		}
		JObject body = new JObject
		{
			["protocolVersion"] = "1",
			["token"] = ((JToken)configuration).Value<string>((object)"token"),
			["batchId"] = batch.BatchId,
			["records"] = (JToken)(object)batch.Records
		};
		UnityWebRequest request = new UnityWebRequest(((JToken)configuration).Value<string>((object)"endpoint"), "POST");
		try
		{
			_request = request;
			request.uploadHandler = (UploadHandler)new UploadHandlerRaw(Encoding.UTF8.GetBytes(((JToken)body).ToString((Formatting)0, Array.Empty<JsonConverter>())));
			request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.timeout = _timeout;
			request.redirectLimit = 5;
			UnityWebRequestAsyncOperation operation = null;
			string startError = null;
			try { operation = request.SendWebRequest(); }
			catch (Exception exception) { startError = exception.GetType().Name; }
			if (startError != null)
			{
				_request = null;
				FailUpload("HTTP 요청 시작 실패: " + startError);
				yield break;
			}
			yield return operation;
			bool accepted = false;
			string responseError = null;
			if ((int)request.result == 1)
			{
				try
				{
					JObject response = JObject.Parse(request.downloadHandler.text);
					responseError = ((JToken)response).Value<string>((object)"error");
					JToken obj = response["acknowledgedEventIds"];
					HashSet<string> acknowledged = new HashSet<string>(((obj != null) ? obj.Values<string>() : null) ?? Array.Empty<string>());
					accepted = ((JToken)response).Value<bool?>((object)"ok") == true && ((JToken)response).Value<string>((object)"batchId") == batch.BatchId;
					if (!accepted && responseError == null) responseError = "response_or_batch_mismatch";
					foreach (JToken record in batch.Records)
					{
						if (!acknowledged.Contains((string)record[(object)"sessionId"] + ":" + (long)record[(object)"eventSequence"]))
						{
							accepted = false;
							if (string.IsNullOrEmpty(responseError)) responseError = "missing_event_ack";
						}
					}
				}
				catch (Exception)
				{
					accepted = false;
					responseError = "non_json_response";
				}
			}
			_request = null;
			if (!accepted)
			{
				FailUpload("Google Sheets upload was not acknowledged. HTTP " + request.responseCode + ", transport=" + request.result + ", server=" + (responseError ?? "none") + ", client=" + (request.error ?? "none") + ", batch=" + batch.BatchId + ", records=" + batch.Records.Count + ", priority=" + priority);
				yield break;
			}
		}
		finally
		{
			if (ReferenceEquals(_request, request)) _request = null;
			((IDisposable)request)?.Dispose();
		}
		if (priority)
		{
			foreach (JToken record in batch.Records)
			{
				string sessionId = (string)record["sessionId"];
				long sequence = (long)record["eventSequence"];
				string key = sessionId + ":" + sequence;
				_priorityRecords.Remove(key);
				if ((!_contiguousAcknowledgedSequences.TryGetValue(sessionId, out long acknowledged) || sequence > acknowledged)
					&& _priorityAcknowledgedKeys.Add(key)) _uploaded++;
			}
		}
		else
		{
			Task checkpoint = _store.AcknowledgeAsync(batch);
			while (!checkpoint.IsCompleted) yield return null;
			if (checkpoint.IsFaulted || checkpoint.IsCanceled)
			{
				FailUpload("전송 커서 저장 실패: " + (checkpoint.Exception?.GetBaseException().Message ?? "cancelled"));
				yield break;
			}
			foreach (JToken record in batch.Records)
			{
				string sessionId = (string)record["sessionId"];
				long sequence = (long)record["eventSequence"];
				string key = sessionId + ":" + sequence;
				_priorityRecords.Remove(key);
				_contiguousAcknowledgedSequences.TryGetValue(sessionId, out long acknowledged);
				if (!_priorityAcknowledgedKeys.Remove(key) && sequence > acknowledged) _uploaded++;
				_contiguousAcknowledgedSequences[sessionId] = Math.Max(acknowledged, sequence);
			}
		}
		_lastError = null;
		_uploading = false;
		_nextUpload = Time.realtimeSinceStartupAsDouble + 0.5;
	}

	/// <summary>
	/// 로컬 또는 기본 전송 설정을 읽고 검증한다.
	/// 저장소 경로를 사용하고 검증된 설정 객체를 반환한다.
	/// </summary>
	private JObject ReadUploadConfiguration()
	{
		string path = Path.Combine(_store.Root, "upload-config.json");
		if (File.Exists(path))
		{
			_configurationSource = "LocalOverride";
			return JObject.Parse(File.ReadAllText(path));
		}
		_configurationSource = "BuiltIn";
		return CombatTelemetryUploadSettings.LoadConfiguration();
	}

	/// <summary>
	/// 업로드 실패를 기록하고 재시도 시각을 설정한다.
	/// 오류 메시지를 사용하며 전송 상태를 변경한다.
	/// </summary>
	private void FailUpload(string message)
	{
		ReportError(message);
		_nextUpload = Time.realtimeSinceStartupAsDouble + 20.0;
		_retryNotBefore = _nextUpload;
		_uploading = false;
	}

	/// <summary>
	/// 최근 오류를 저장하고 Unity 로그에 경고를 남긴다.
	/// 오류 메시지를 사용하며 진단 상태를 변경한다.
	/// </summary>
	private void ReportError(string message)
	{
		_lastError = message;
		if (!(_lastWarnedError == message))
		{
			_lastWarnedError = message;
			Debug.LogWarning((object)("전투 로그: " + message), (UnityEngine.Object)(object)this);
		}
	}

	/// <summary>
	/// 종료 전 로그 저장과 재시도 대기를 건너뛴 전송을 요청한다.
	/// 종료 요청 상태를 확인하고 종료 허용 여부를 반환한다.
	/// </summary>
	private bool HandleWantsToQuit()
	{
		if (_quitAllowed)
		{
			return true;
		}
		if (_quitRequested)
		{
			return false;
		}
		_quitRequested = true;
		Application.runInBackground = true;
		NotifyQuitRequested();
		RequestFlushImmediately();
		((MonoBehaviour)this).StartCoroutine(DrainBeforeQuit());
		return false;
	}

	/// <summary>
	/// 종료 요청 이벤트를 한 번 발생시킨다.
	/// 이벤트 구독자와 알림 상태를 사용한다.
	/// </summary>
	private void NotifyQuitRequested()
	{
		if (_quitNotified)
		{
			return;
		}
		_quitNotified = true;
		Action handlers = this.QuitRequested;
		if (handlers != null)
		{
			Delegate[] invocationList = handlers.GetInvocationList();
			foreach (Delegate subscriber in invocationList)
			{
				try
				{
					((Action)subscriber)();
				}
				catch (Exception ex)
				{
					ReportError("종료 기록자 처리 실패: " + ex.Message);
				}
			}
		}
		EnsureApplicationQuitRecord();
	}

	/// <summary>
	/// 앱 종료 이벤트가 아직 없으면 종료 기록을 버퍼에 추가한다.
	/// 최근 시도 기록과 세션 상태를 사용하며 버퍼를 변경한다.
	/// </summary>
	private void EnsureApplicationQuitRecord()
	{
		if (_latestAttemptRecord == null)
		{
			return;
		}
		JObject previousPayload = ((JToken)_latestAttemptRecord).Value<JObject>((object)"payload");
		if (!(((JToken)_latestAttemptRecord).Value<string>((object)"eventType") == "session_ended") || !(((previousPayload != null) ? ((JToken)previousPayload).Value<string>((object)"reason") : null) == "application_quit"))
		{
			long sequence = ((JToken)_latestAttemptRecord).Value<long?>((object)"eventSequence").GetValueOrDefault() + 1;
			string now = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
			JObject record = (JObject)((JToken)_latestAttemptRecord).DeepClone();
			record["eventSequence"] = sequence;
			record["sourceId"] = "telemetry.lifecycle";
			record["sourceSequence"] = sequence;
			record["eventType"] = "session_ended";
			record["occurredAtKst"] = now;
			record["recordedAtKst"] = now;
			JObject previousTime = ((JToken)_latestAttemptRecord).Value<JObject>((object)"occurredAt");
			double elapsed = Math.Max(0.0, Time.realtimeSinceStartupAsDouble - _latestAttemptObservedRealtime);
			JObject val = new JObject { ["realtimeSeconds"] = (double?)((previousTime != null) ? previousTime["realtimeSeconds"] : null) + elapsed };
			object obj;
			if (previousTime == null)
			{
				obj = null;
			}
			else
			{
				JToken obj2 = previousTime["gameplaySeconds"];
				obj = ((obj2 != null) ? obj2.DeepClone() : null);
			}
			if (obj == null)
			{
				obj = (object)new JValue(0.0);
			}
			val["gameplaySeconds"] = (JToken)obj;
			object obj3;
			if (previousTime == null)
			{
				obj3 = null;
			}
			else
			{
				JToken obj4 = previousTime["activeUnscaledSeconds"];
				obj3 = ((obj4 != null) ? obj4.DeepClone() : null);
			}
			if (obj3 == null)
			{
				obj3 = (object)new JValue(0.0);
			}
			val["activeUnscaledSeconds"] = (JToken)obj3;
			record["occurredAt"] = (JToken)val;
			record["recordedAt"] = (JToken)(JObject)record["occurredAt"].DeepClone();
			record["correlation"] = (JToken)new JObject
			{
				["inputId"] = 0,
				["actionId"] = 0,
				["attackId"] = 0,
				["hitId"] = 0,
				["projectileId"] = 0,
				["dodgeId"] = 0
			};
			string stageId = ((JToken)record).Value<string>((object)"stageId");
			string attemptId = ((JToken)record).Value<string>((object)"stageAttemptId");
			record["payload"] = (JToken)new JObject
			{
				["reason"] = "application_quit",
				["exitStageId"] = stageId,
				["exitStageAttemptId"] = attemptId,
				["exitLocation"] = stageId,
				["exitOccurredAtKst"] = now
			};
			Enqueue(record);
			RequestFlush();
		}
	}

	/// <summary>
	/// 종료 기록을 저장한 뒤 Windows 독립 업로더에 전송을 인계한다.
	/// 현재 실행의 세션과 준비 파일을 사용하며 인계 실패 시 최대 8초까지 직접 ACK를 기다린다.
	/// </summary>
	private IEnumerator DrainBeforeQuit()
	{
		double deadline = Time.realtimeSinceStartupAsDouble + QUIT_UPLOAD_TIMEOUT_SECONDS;
		yield return null;
		while (Time.realtimeSinceStartupAsDouble < deadline && (_write != null || _buffer.Count != 0))
			yield return null;
		string readyPath = null;
		if (_write == null && _buffer.Count == 0 && _allowUpload && !_uploadDisabled
			&& Application.platform == RuntimePlatform.WindowsPlayer)
		{
			string error = null;
			try
			{
				CombatTelemetryBackgroundUpload.TrySchedule(_store.Root, ReadUploadConfiguration(),
					new HashSet<string>(_pendingUploadSessionIds), _batchLimit, _timeout, out readyPath, out error);
			}
			catch (Exception exception)
			{
				error = "종료 전송 설정 확인 실패: " + exception.GetType().Name;
			}
			if (!string.IsNullOrEmpty(error)) ReportError(error);
			if (readyPath != null)
			{
				double readyDeadline = Math.Min(deadline, Time.realtimeSinceStartupAsDouble + BACKGROUND_START_TIMEOUT_SECONDS);
				while (!File.Exists(readyPath) && Time.realtimeSinceStartupAsDouble < readyDeadline) yield return null;
			}
		}
		while (Time.realtimeSinceStartupAsDouble < deadline && (_write != null || _writing != null || _buffer.Count != 0 || _uploading || (!_diskDrained && _allowUpload && !_uploadDisabled)))
		{
			if (readyPath != null && File.Exists(readyPath)) break;
			yield return null;
		}
		if (readyPath != null && File.Exists(readyPath))
		{
		}
		else if (!_diskDrained && _allowUpload && !_uploadDisabled)
		{
			ReportError("종료 전송 제한 시간 초과: ACK되지 않은 로그와 종료 후 전송 요청을 로컬에 보존했습니다. background-upload 요청 파일과 전송기 로그를 확인하세요.");
		}
		_quitAllowed = true;
		Application.Quit();
	}

	/// <summary>
	/// 종료 전에 메모리의 미저장 이벤트를 로컬 JSONL에 기록한다.
	/// 현재 버퍼와 쓰기 작업을 사용하며 디스크 저장 상태를 변경한다.
	/// </summary>
	private void PersistPendingEventsOnShutdown()
	{
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500.0);
		if (_write != null)
		{
			Task activeWrite = _write;
			if (!WaitForTaskUntil(activeWrite, deadline))
			{
				ReportError("종료 로컬 저장 제한 시간을 넘어 미전송 로그를 다음 실행에 보존하지 못했을 수 있습니다.");
				return;
			}
			if ((activeWrite.IsFaulted || activeWrite.IsCanceled) && _writing != null)
			{
				_buffer.InsertRange(0, _writing);
				ReportError("종료 중 로컬 저장 실패: " + (activeWrite.Exception?.GetBaseException().Message ?? "cancelled"));
			}
			else if (_writing != null)
				foreach (string line in _writing) TrackPriorityRecord(JObject.Parse(line));
			_write = null;
			_writing = null;
		}
		if (_buffer.Count != 0)
		{
			string[] pending = _buffer.ToArray();
			_buffer.Clear();
			Task finalWrite = _store.AppendAsync(pending);
			if (!WaitForTaskUntil(finalWrite, deadline))
			{
				_buffer.InsertRange(0, pending);
				ReportError("종료 로컬 저장 제한 시간을 넘어 미전송 로그를 다음 실행에 보존하지 못했을 수 있습니다.");
			}
			else if (finalWrite.IsFaulted || finalWrite.IsCanceled)
			{
				_buffer.InsertRange(0, pending);
				ReportError("종료 중 로컬 저장 실패: " + (finalWrite.Exception?.GetBaseException().Message ?? "cancelled"));
			}
			else
			{
				foreach (string line in pending) TrackPriorityRecord(JObject.Parse(line));
				if (!string.IsNullOrEmpty(_store.MetadataError)) ReportError(_store.MetadataError);
			}
		}
	}

	/// <summary>
	/// 작업이 마감 시각 전에 완료되는지 기다린다.
	/// 작업과 마감 시각을 사용하고 완료 여부를 반환한다.
	/// </summary>
	private static bool WaitForTaskUntil(Task task, DateTime deadline)
	{
		int timeoutMilliseconds = (int)Math.Max(0.0, (deadline - DateTime.UtcNow).TotalMilliseconds);
		return Task.WaitAny(new Task[1] { task }, timeoutMilliseconds) == 0;
	}

#if UNITY_EDITOR
	/// <summary>
	/// 에디터 플레이 모드 종료 시 기록을 저장하고 현재 세션 업로드를 예약한다.
	/// PlayModeStateChange를 사용하며 종료 기록과 업로드 요청을 변경한다.
	/// </summary>
	private void HandleEditorPlayModeChanged(PlayModeStateChange state)
	{
		if ((int)state == 3)
		{
			NotifyQuitRequested();
			RequestFlush();
			PersistPendingEventsOnShutdown();
			CombatTelemetryEditorUploader.Schedule(_store.Root, _allowUpload, _batchLimit, _timeout,
				new HashSet<string>(_pendingUploadSessionIds), _priorityRecords.Values);
		}
	}

#endif

	private void OnApplicationPause(bool paused)
	{
		if (paused)
		{
			RequestFlushImmediately();
		}
	}

	private void OnApplicationQuit()
	{
		NotifyQuitRequested();
		if (!_quitRequested)
		{
			RequestFlush();
		}
		UnityWebRequest request = _request;
		if (request != null)
		{
			request.Abort();
		}
	}

	private void OnDestroy()
	{
		if (!Application.isEditor)
		{
			Application.wantsToQuit -= HandleWantsToQuit;
		}
		#if UNITY_EDITOR
		EditorApplication.playModeStateChanged -= HandleEditorPlayModeChanged;
#endif
		NotifyQuitRequested();
		_quitting = true;
		UnityWebRequest request = _request;
		if (request != null)
		{
			request.Abort();
		}
		try
		{
			PersistPendingEventsOnShutdown();
		}
		catch (Exception ex)
		{
			_lastError = "종료 저장 실패: " + ex.GetBaseException().Message;
		}
		finally
		{
			if ((UnityEngine.Object)(object)_active == (UnityEngine.Object)(object)this)
			{
				_active = null;
			}
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		_active = null;
	}
}

#if UNITY_EDITOR
internal static class CombatTelemetryEditorUploader
{
	private const int MAXIMUM_BATCH_BYTES = 196608;

	private const long EDITOR_STORAGE_BUDGET_BYTES = 104857600L;

	private const double RETRY_INTERVAL_SECONDS = 20.0;

	private const string REQUEST_FILE_NAME = "editor-upload-request.json";

	private static TelemetryDiskStore _store;

	private static JObject _configuration;

	private static Task<UploadBatch> _reading;

	private static Task _checkpoint;

	private static UnityWebRequest _request;

	private static UnityWebRequestAsyncOperation _operation;

	private static UploadBatch _batch;

	private static Dictionary<string, JObject> _priorityRecords;

	private static string _destination;

	private static string _requestPath;

	private static string _lastError;

	private static HashSet<string> _sessionIds;

	private static double _retryNotBefore;

	private static int _batchLimit;

	private static int _timeout;

	private static int _acknowledgedEvents;

	private static bool _running;


	/// <summary>
	/// 에디터 업로더의 플레이 모드 콜백을 등록하고 기존 요청을 확인한다.
	/// 에디터 이벤트와 예약된 업로드 요청을 사용하며 콜백 등록 상태를 변경한다.
	/// </summary>
	[InitializeOnLoadMethod]
	private static void Initialize()
	{
		EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
		EditorApplication.playModeStateChanged += HandlePlayModeChanged;
		EditorApplication.delayCall = (EditorApplication.CallbackFunction)Delegate.Combine((Delegate?)(object)EditorApplication.delayCall, (Delegate?)new EditorApplication.CallbackFunction(StartPendingUpload));
	}

	/// <summary>
	/// 완료된 플레이 세션만 대상으로 하는 에디터 업로드 요청을 저장한다.
	/// 저장 경로, 업로드 설정, 배치 제한, 세션 ID를 사용해 요청 파일을 변경한다.
	/// </summary>
	internal static void Schedule(string root, bool allowUpload, int batchLimit, int timeout, HashSet<string> sessionIds,
		IEnumerable<JObject> priorityRecords)
	{
		if (string.IsNullOrEmpty(root) || sessionIds == null || sessionIds.Count == 0)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(root);
			string text = Path.Combine(root, "editor-upload-request.json");
			string text2 = text + ".tmp";
			JObject val = new JObject
			{
				["allowUpload"] = allowUpload,
				["batchLimit"] = Math.Max(1, Math.Min(100, batchLimit)),
				["timeout"] = Math.Max(5, Math.Min(120, timeout)),
				["sessionIds"] = (JToken)new JArray((object)sessionIds),
				["priorityRecords"] = new JArray(priorityRecords)
			};
			File.WriteAllText(text2, ((JToken)val).ToString((Formatting)0, Array.Empty<JsonConverter>()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			if (File.Exists(text))
			{
				File.Replace(text2, text, null);
			}
			else
			{
				File.Move(text2, text);
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("CombatTelemetry Editor upload request could not be saved: " + ex.GetBaseException().GetType().Name));
			return;
		}
		EditorApplication.delayCall = (EditorApplication.CallbackFunction)Delegate.Combine((Delegate?)(object)EditorApplication.delayCall, (Delegate?)new EditorApplication.CallbackFunction(StartPendingUpload));
	}

	/// <summary>
	/// 저장된 업로드 요청이 있을 때 지정 세션의 전송을 시작한다.
	/// 요청 파일과 업로드 설정을 사용하며 에디터 업로드 상태를 변경한다.
	/// </summary>
	private static void StartPendingUpload()
	{
		if (_running || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}
		string text = Path.Combine(Application.persistentDataPath, "CombatTelemetry");
		if (!Directory.Exists(text))
		{
			return;
		}
		string text2 = Path.Combine(text, "editor-upload-request.json");
		if (!File.Exists(text2))
		{
			return;
		}
		int num = 100;
		int num2 = 30;
		HashSet<string> hashSet = new HashSet<string>();
		JArray priorityRecords;
		bool flag;
		try
		{
			JObject val = JObject.Parse(File.ReadAllText(text2, Encoding.UTF8));
			priorityRecords = val["priorityRecords"] as JArray ?? new JArray();
			flag = ((JToken)val).Value<bool?>((object)"allowUpload") == true;
			num = Math.Max(1, Math.Min(100, ((JToken)val).Value<int?>((object)"batchLimit") ?? 100));
			num2 = Math.Max(5, Math.Min(120, ((JToken)val).Value<int?>((object)"timeout") ?? 30));
			JToken obj = val["sessionIds"];
			JArray val2 = (JArray)(object)((obj is JArray) ? obj : null);
			if (val2 != null)
			{
				foreach (JToken item in val2)
				{
					string text3 = Extensions.Value<string>((IEnumerable<JToken>)item);
					if (Guid.TryParseExact(text3, "N", out var _))
					{
						hashSet.Add(text3);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("CombatTelemetry Editor upload request is unreadable: " + ex.GetBaseException().GetType().Name));
			return;
		}
		if (flag && hashSet.Count != 0)
		{
			StartUpload(text, text2, num, num2, hashSet, priorityRecords);
		}
	}

	/// <summary>
	/// 에디터 플레이 모드 전환에 맞춰 업로드를 멈추거나 재개한다.
	/// PlayModeStateChange를 사용하며 에디터 업로드 상태를 변경한다.
	/// </summary>
	private static void HandlePlayModeChanged(PlayModeStateChange state)
	{
		if ((int)state == 1)
		{
			PauseForPlayMode();
		}
		else if ((int)state == 0)
		{
			StartPendingUpload();
		}
	}

	/// <summary>
	/// 플레이 모드 진입 시 진행 중인 에디터 업로드 요청을 정리한다.
	/// 현재 요청과 저장 작업을 사용하며 업로더 상태를 초기화한다.
	/// </summary>
	private static void PauseForPlayMode()
	{
		if (_running)
		{
			if (_request != null)
			{
				_request.Abort();
				_request.Dispose();
			}
			if (_checkpoint != null && !_checkpoint.IsCompleted)
			{
				Task.WaitAny(new Task[1] { _checkpoint }, 1000);
			}
			_running = false;
			EditorApplication.update = (EditorApplication.CallbackFunction)Delegate.Remove((Delegate?)(object)EditorApplication.update, (Delegate?)new EditorApplication.CallbackFunction(Pump));
			_store = null;
			_configuration = null;
			_reading = null;
			_checkpoint = null;
			_request = null;
			_operation = null;
			_batch = null;
			_priorityRecords = null;
			_sessionIds = null;
			_destination = null;
			_requestPath = null;
		}
	}

	/// <summary>
	/// 검증된 설정과 세션 목록으로 에디터 업로드를 초기화한다.
	/// 저장 경로, 요청 경로, 배치 제한, 시간 제한, 세션 ID를 사용하며 업로더 상태를 변경한다.
	/// </summary>
	private static void StartUpload(string root, string requestPath, int batchLimit, int timeout, HashSet<string> sessionIds,
		JArray priorityRecords)
	{
		JObject val;
		try
		{
			string path = Path.Combine(root, "upload-config.json");
			val = (File.Exists(path) ? JObject.Parse(File.ReadAllText(path, Encoding.UTF8)) : CombatTelemetryUploadSettings.LoadConfiguration());
			if (((JToken)val).Value<bool?>((object)"enabled") != true)
			{
				return;
			}
			if (!Uri.TryCreate(((JToken)val).Value<string>((object)"endpoint"), UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps || (((JToken)val).Value<string>((object)"token")?.Length ?? 0) < 32)
			{
				throw new InvalidDataException("invalid_upload_configuration");
			}
			using SHA256 sHA = SHA256.Create();
			_destination = BitConverter.ToString(sHA.ComputeHash(Encoding.UTF8.GetBytes(((JToken)val).Value<string>((object)"endpoint")))).Replace("-", "").Substring(0, 32);
		}
		catch (Exception ex)
		{
			Debug.LogWarning((object)("CombatTelemetry Editor upload configuration is invalid: " + ex.GetBaseException().GetType().Name));
			return;
		}
		_store = new TelemetryDiskStore(root, 104857600L);
		_configuration = val;
		_requestPath = requestPath;
		_sessionIds = sessionIds;
		_priorityRecords = new Dictionary<string, JObject>();
		foreach (JObject record in priorityRecords)
		{
			string sessionId = record.Value<string>("sessionId");
			if (sessionIds.Contains(sessionId))
				_priorityRecords[sessionId + ":" + record.Value<long>("eventSequence")] = record;
		}
		_batchLimit = Math.Max(1, Math.Min(100, batchLimit));
		_timeout = Math.Max(5, Math.Min(120, timeout));
		_retryNotBefore = 0.0;
		_acknowledgedEvents = 0;
		_lastError = null;
		_running = true;
		EditorApplication.update = (EditorApplication.CallbackFunction)Delegate.Remove((Delegate?)(object)EditorApplication.update, (Delegate?)new EditorApplication.CallbackFunction(Pump));
		EditorApplication.update = (EditorApplication.CallbackFunction)Delegate.Combine((Delegate?)(object)EditorApplication.update, (Delegate?)new EditorApplication.CallbackFunction(Pump));
	}

	/// <summary>
	/// 에디터 업데이트마다 로컬 배치 읽기, HTTP 응답, ACK 커서를 진행한다.
	/// 현재 작업 상태를 사용하며 업로드 진행 상태를 변경한다.
	/// </summary>
	private static void Pump()
	{
		if (!_running)
		{
			return;
		}
		if (EditorApplication.isPlayingOrWillChangePlaymode)
		{
			PauseForPlayMode();
		}
		else if (_reading != null)
		{
			if (!_reading.IsCompleted)
			{
				return;
			}
			if (_reading.IsFaulted)
			{
				_reading = null;
				FailUpload("local batch read failed");
				return;
			}
			UploadBatch result = _reading.Result;
			_reading = null;
			if (result == null)
			{
				if (!string.IsNullOrEmpty(_store.ReadError))
				{
					FailUpload("local batch read failed");
				}
				else
				{
					FinishUpload();
				}
			}
			else
			{
				_batch = result;
				SendBatch();
			}
		}
		else if (_request != null)
		{
			if (_operation != null && ((AsyncOperation)_operation).isDone)
			{
				if (!IsAcknowledged(_request, _batch, out var responseError))
				{
					string message = "HTTP " + _request.responseCode + ", transport=" + ((object)_request.result/*cast due to .constrained prefix*/).ToString() + ", server=" + (responseError ?? "none") + ", client=" + (_request.error ?? "none");
					DisposeRequest(abort: false);
					FailUpload(message);
				}
				else
				{
					DisposeRequest(abort: false);
					if (string.IsNullOrEmpty(_batch.CursorPath))
					{
						foreach (JToken record in _batch.Records)
							_priorityRecords.Remove((string)record["sessionId"] + ":" + (long)record["eventSequence"]);
						_batch = null;
						_retryNotBefore = EditorApplication.timeSinceStartup + 0.5;
					}
					else _checkpoint = _store.AcknowledgeAsync(_batch);
				}
			}
		}
		else if (_checkpoint != null)
		{
			if (!_checkpoint.IsCompleted)
			{
				return;
			}
			if (_checkpoint.IsFaulted)
			{
				_checkpoint = null;
				FailUpload("ACK cursor save failed");
				return;
			}
			_acknowledgedEvents += ((JContainer)_batch.Records).Count;
			_checkpoint = null;
			_batch = null;
			_lastError = null;
			_retryNotBefore = EditorApplication.timeSinceStartup + 0.5;
		}
		else if (!(EditorApplication.timeSinceStartup < _retryNotBefore))
		{
			_batch = UploadBatch.CreatePriority(_priorityRecords.Values, _batchLimit, MAXIMUM_BATCH_BYTES);
			if (_batch != null) SendBatch();
			else _reading = _store.ReadBatchAsync(_batchLimit, MAXIMUM_BATCH_BYTES, _destination, _sessionIds);
		}
	}

	/// <summary>
	/// 현재 로컬 배치를 Apps Script endpoint로 전송한다.
	/// 설정과 배치 내용을 사용하며 UnityWebRequest 상태를 변경한다.
	/// </summary>
	private static void SendBatch()
	{
		try
		{
			JObject val = new JObject
			{
				["protocolVersion"] = "1",
				["token"] = ((JToken)_configuration).Value<string>((object)"token"),
				["batchId"] = _batch.BatchId,
				["records"] = (JToken)(object)_batch.Records
			};
			_request = new UnityWebRequest(((JToken)_configuration).Value<string>((object)"endpoint"), "POST")
			{
				uploadHandler = (UploadHandler)new UploadHandlerRaw(Encoding.UTF8.GetBytes(((JToken)val).ToString((Formatting)0, Array.Empty<JsonConverter>()))),
				downloadHandler = (DownloadHandler)new DownloadHandlerBuffer(),
				timeout = _timeout,
				redirectLimit = 5
			};
			_request.SetRequestHeader("Content-Type", "application/json");
			_operation = _request.SendWebRequest();
		}
		catch (Exception ex)
		{
			DisposeRequest(abort: true);
			FailUpload("HTTP request could not start: " + ex.GetBaseException().GetType().Name);
		}
	}

	/// <summary>
	/// HTTP 응답의 batch ID와 이벤트별 ACK가 배치와 일치하는지 확인한다.
	/// 요청과 배치를 사용하고 서버 오류와 수락 여부를 반환한다.
	/// </summary>
	private static bool IsAcknowledged(UnityWebRequest request, UploadBatch batch, out string responseError)
	{
		responseError = null;
		if ((int)request.result != 1)
		{
			return false;
		}
		try
		{
			JObject val = JObject.Parse(request.downloadHandler.text);
			responseError = ((JToken)val).Value<string>((object)"error");
			JToken obj = val["acknowledgedEventIds"];
			HashSet<string> hashSet = new HashSet<string>(((obj != null) ? obj.Values<string>() : null) ?? Array.Empty<string>());
			bool flag = ((JToken)val).Value<bool?>((object)"ok") == true && ((JToken)val).Value<string>((object)"batchId") == batch.BatchId;
			if (!flag && string.IsNullOrEmpty(responseError)) responseError = "response_or_batch_mismatch";
			foreach (JToken record in batch.Records)
			{
				if (!hashSet.Contains((string)record["sessionId"] + ":" + (long)record["eventSequence"]))
				{
					flag = false;
					if (string.IsNullOrEmpty(responseError)) responseError = "missing_event_ack";
				}
			}
			return flag;
		}
		catch (Exception)
		{
			responseError = "non_json_response";
			return false;
		}
	}

	/// <summary>
	/// 에디터 업로드 실패를 기록하고 고정 간격 재시도를 예약한다.
	/// 오류 메시지를 사용하며 오류와 재시도 상태를 변경한다.
	/// </summary>
	private static void FailUpload(string message)
	{
		if (_lastError != message)
		{
			_lastError = message;
			Debug.LogWarning((object)("CombatTelemetry Editor upload failed: " + message + "; local events remain queued for retry."));
		}
		_retryNotBefore = EditorApplication.timeSinceStartup + 20.0;
		_batch = null;
		_checkpoint = null;
		_reading = null;
	}

	/// <summary>
	/// 현재 HTTP 요청을 선택적으로 중단하고 자원을 해제한다.
	/// abort 여부를 사용하며 요청과 비동기 작업 참조를 초기화한다.
	/// </summary>
	private static void DisposeRequest(bool abort)
	{
		if (_request != null)
		{
			if (abort)
			{
				_request.Abort();
			}
			_request.Dispose();
		}
		_request = null;
		_operation = null;
	}

	/// <summary>
	/// 대상 세션 전송을 마치고 요청 파일과 에디터 상태를 정리한다.
	/// 업로드 결과를 사용하며 요청 파일과 진행 상태를 변경한다.
	/// </summary>
	private static void FinishUpload()
	{
		_running = false;
		EditorApplication.update = (EditorApplication.CallbackFunction)Delegate.Remove((Delegate?)(object)EditorApplication.update, (Delegate?)new EditorApplication.CallbackFunction(Pump));
		if (File.Exists(_requestPath))
		{
			File.Delete(_requestPath);
		}
		_store = null;
		_configuration = null;
		_sessionIds = null;
		_requestPath = null;
		_destination = null;
		_priorityRecords = null;
	}
}
#endif

}
