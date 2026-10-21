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

namespace BlindBlade.Telemetry
{
    /// <summary>씬을 넘어 원본 저장·디스크 기반 재전송을 유지한다. 전투 시스템에는 결과를 돌려주지 않는다.</summary>
    public sealed class CombatTelemetryService : MonoBehaviour
    {
        private const int MAXIMUM_BATCH_BYTES = 196608;
        private const double RETRY_INTERVAL_SECONDS = 20d;
        private const double QUIT_UPLOAD_TIMEOUT_SECONDS = 35d;
        private static CombatTelemetryService _active;

        private readonly List<string> _buffer = new List<string>();
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
        private bool _flushRequested;
        private bool _diskDrained;
        private bool _uploadDisabled;
        private bool _quitAllowed;
        private bool _allowUpload;
        private bool _uploading;
        private bool _quitting;
        private bool _quitRequested;
        private bool _quitNotified;
        private bool _reportedUploadSuccess;
        private string _lastError;
        private string _lastWarnedError;
        private int _dropped;
        private int _uploaded;
        private string _anonymousPlayerId;
        private string _configurationSource;

        public static CombatTelemetryService Active => _active;
        public string LogDirectory => _store?.Root;
        public string LastError => _lastError;
        public int DroppedEvents => _dropped;
        public int AcknowledgedEvents => _uploaded;
        public int BufferedEvents => _buffer.Count + (_writing?.Length ?? 0);
        public string AnonymousPlayerId => _anonymousPlayerId;
        public string ConfigurationSource => _configurationSource;
        public event Action QuitRequested;

        /// <summary>
        /// 저장 한도, 전송 간격, 배치 크기와 제한 시간 설정을 사용해 영속 서비스를 만들거나 기존 서비스를 반환한다.
        /// 생성된 서비스 상태는 씬 전환과 재도전 뒤에도 유지된다.
        /// </summary>
        public static CombatTelemetryService GetOrCreate(int bufferLimit, float flushInterval, int maximumMegabytes,
            bool allowUpload, float uploadInterval, int batchLimit, int timeout)
        {
            if (_active != null) return _active;
            GameObject host = new GameObject("CombatTelemetryService");
            DontDestroyOnLoad(host);
            _active = host.AddComponent<CombatTelemetryService>();
            _active._store = new TelemetryDiskStore(Path.Combine(Application.persistentDataPath, "CombatTelemetry"),
                Math.Max(1, maximumMegabytes) * 1024L * 1024L);
            _active._startupRecovery = _active._store.RecoverInterruptedSessionsAsync();
            _active._bufferLimit = Math.Max(64, bufferLimit);
            _active._flushInterval = Mathf.Max(0.2f, flushInterval);
            _active._uploadInterval = Mathf.Max(5f, uploadInterval);
            _active._batchLimit = Mathf.Clamp(batchLimit, 1, 100);
            _active._timeout = Mathf.Clamp(timeout, 5, 120);
            _active._allowUpload = allowUpload;
            if (!Application.isEditor) Application.wantsToQuit += _active.HandleWantsToQuit;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += _active.HandleEditorPlayModeChanged;
#endif
            _active._anonymousPlayerId = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(_active._store.Root);
                string identityPath = Path.Combine(_active._store.Root, "anonymous-player-id.txt");
                if (File.Exists(identityPath) && Guid.TryParseExact(File.ReadAllText(identityPath).Trim(), "N", out Guid identity))
                    _active._anonymousPlayerId = identity.ToString("N");
                else File.WriteAllText(identityPath, _active._anonymousPlayerId);
            }
            catch (Exception exception) { _active.ReportError("익명 ID 저장 실패: " + exception.Message); }
            return _active;
        }

        /// <summary>record를 즉시 문자열 스냅샷으로 버퍼에 넣는다. 한도 초과 시 일반 사건은 거부하고 종료 사건은 보존한다.</summary>
        internal void Enqueue(JObject record)
        {
            if (_quitting) return;
            string eventType = record.Value<string>("eventType");
            bool terminal = eventType == "player_died" || eventType == "stage_ended"
                || eventType == "session_ended" || eventType == "game_completed";
            if (BufferedEvents >= _bufferLimit && !terminal)
            {
                _dropped++;
                ReportError("로그 메모리 한도를 넘어 사건이 누락되었습니다.");
                return;
            }
            _buffer.Add(record.ToString(Formatting.None));
            _recordVersion++;
            _diskDrained = false;
        }

        /// <summary>현재 버퍼의 저장과 후속 전송을 요청한다. 진행 중인 전송 중에도 요청을 보존하며 실패 재시도 간격은 유지한다.</summary>
        public void RequestFlush()
        {
            _flushRequested = true;
            _diskDrained = false;
            _nextFlush = 0d;
        }

        void Update()
        {
            if (_startupRecovery != null)
            {
                if (!_startupRecovery.IsCompleted) return;
                if (_startupRecovery.IsFaulted)
                    ReportError("이전 세션 복구 실패: " + _startupRecovery.Exception.GetBaseException().Message);
                else if (_startupRecovery.IsCanceled)
                    ReportError("이전 세션 복구 작업이 취소되었습니다. 원본 로그를 보존하고 전송을 계속합니다.");
                else if (!string.IsNullOrEmpty(_startupRecovery.Result))
                    ReportError(_startupRecovery.Result);
                _startupRecovery = null;
            }

            ObserveWrite();
            double now = Time.realtimeSinceStartupAsDouble;
            if (_write == null && _buffer.Count > 0 && now >= _writeRetryNotBefore
                && (_flushRequested || now >= _nextFlush))
            {
                _writing = _buffer.ToArray();
                _buffer.Clear();
                _write = _store.AppendAsync(_writing);
                _nextFlush = now + _flushInterval;
            }
            if (_allowUpload && !_uploading && _write == null && _writing == null && _buffer.Count == 0
                && now >= _retryNotBefore
                && (_flushRequested || now >= _nextUpload))
            {
                _flushRequested = false;
                StartCoroutine(Upload());
            }
        }

        /// <summary>완료된 저장 작업의 오류를 확인한다. 실패한 사건을 버퍼 앞에 복구하고 20초 뒤 원본 저장을 재시도한다.</summary>
        private void ObserveWrite()
        {
            if (_write == null || !_write.IsCompleted) return;
            if (_write.IsFaulted)
            {
                if (_writing != null && _writing.Length > 0)
                {
                    _buffer.InsertRange(0, _writing);
                    _flushRequested = true;
                }
                _writeRetryNotBefore = Time.realtimeSinceStartupAsDouble + RETRY_INTERVAL_SECONDS;
                _diskDrained = false;
                ReportError("로컬 저장 실패(일부 저장 가능): " + _write.Exception.GetBaseException().Message
                    + "; 원본 저장을 20초 뒤 다시 시도합니다.");
            }
            else
            {
                _writeRetryNotBefore = 0d;
                if (!string.IsNullOrEmpty(_store.MetadataError)) ReportError(_store.MetadataError);
            }
            _write = null;
            _writing = null;
        }

        /// <summary>로컬 재정의 또는 빌드 기본 설정으로 디스크의 미확인 배치를 전송한다. 정확한 사건 키 확인 뒤에만 커서를 이동한다.</summary>
        private IEnumerator Upload()
        {
            _uploading = true;
            _nextUpload = Time.realtimeSinceStartupAsDouble + _uploadInterval;
            JObject configuration;
            try
            {
                configuration = ReadUploadConfiguration();
                _uploadDisabled = configuration.Value<bool?>("enabled") != true;
                if (_uploadDisabled) { _uploading = false; yield break; }
                if (!Uri.TryCreate(configuration.Value<string>("endpoint"), UriKind.Absolute, out Uri endpoint)
                    || endpoint.Scheme != Uri.UriSchemeHttps || (configuration.Value<string>("token")?.Length ?? 0) < 32)
                    throw new InvalidDataException("전송 설정의 HTTPS endpoint와 32자 이상 token을 설정하세요. 설정 출처: " + _configurationSource);
            }
            catch (Exception exception)
            {
                FailUpload("전송 설정 오류: " + exception.Message);
                yield break;
            }

            string destination;
            using (SHA256 algorithm = SHA256.Create())
                destination = BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(configuration.Value<string>("endpoint"))))
                    .Replace("-", "").Substring(0, 32);
            long readVersion = _recordVersion;
            Task<UploadBatch> read = _store.ReadBatchAsync(_batchLimit, MAXIMUM_BATCH_BYTES, destination);
            while (!read.IsCompleted) yield return null;
            if (read.IsFaulted)
            {
                FailUpload("미전송 로그 읽기 실패: " + read.Exception.GetBaseException().Message);
                yield break;
            }
            if (!string.IsNullOrEmpty(_store.ReadError)) ReportError(_store.ReadError);
            UploadBatch batch = read.Result;
            if (batch == null)
            {
                if (!string.IsNullOrEmpty(_store.ReadError))
                {
                    FailUpload(_store.ReadError);
                    yield break;
                }
                _diskDrained = readVersion == _recordVersion && _write == null && _buffer.Count == 0;
                _uploading = false;
                yield break;
            }
            JObject body = new JObject { ["protocolVersion"] = "1", ["token"] = configuration.Value<string>("token"),
                ["batchId"] = batch.BatchId, ["records"] = batch.Records };
            using (UnityWebRequest request = new UnityWebRequest(configuration.Value<string>("endpoint"), "POST"))
            {
                _request = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = _timeout;
                request.redirectLimit = 5;
                yield return request.SendWebRequest();
                bool accepted = false;
                string responseError = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        JObject response = JObject.Parse(request.downloadHandler.text);
                        responseError = response.Value<string>("error");
                        HashSet<string> acknowledged = new HashSet<string>(response["acknowledgedEventIds"]?.Values<string>()
                            ?? Array.Empty<string>());
                        accepted = response.Value<bool?>("ok") == true && response.Value<string>("batchId") == batch.BatchId;
                        foreach (JToken record in batch.Records)
                            accepted &= acknowledged.Contains((string)record["sessionId"] + ":" + (long)record["eventSequence"]);
                    }
                    catch (Exception) { accepted = false; }
                }
                _request = null;
                if (!accepted)
                {
                    FailUpload("Google Sheets upload was not acknowledged. HTTP " + request.responseCode
                        + ", transport=" + request.result + ", server=" + (responseError ?? "none")
                        + ", client=" + (request.error ?? "none"));
                    yield break;
                }
            }
            Task checkpoint = _store.AcknowledgeAsync(batch);
            while (!checkpoint.IsCompleted) yield return null;
            if (checkpoint.IsFaulted)
            {
                FailUpload("전송 커서 저장 실패: " + checkpoint.Exception.GetBaseException().Message);
                yield break;
            }
            _uploaded += batch.Records.Count;
            _lastError = null;
            if (!_reportedUploadSuccess)
            {
                _reportedUploadSuccess = true;
                Debug.Log("CombatTelemetry upload acknowledged. Configuration=" + _configurationSource, this);
            }
            _uploading = false;
            _nextUpload = Time.realtimeSinceStartupAsDouble + 0.5d;
        }

        /// <summary>로컬 파일이 있으면 그 값을 반환하고 없으면 Resources의 기본값을 반환한다. 출처를 보관하며 CONFIG를 생성하지 않는다.</summary>
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
        /// message를 진단에 남기고 20초 뒤 재시도하도록 설정한다.
        /// _lastError, _nextUpload, _uploading을 갱신하며 원본과 확인 커서는 유지한다.
        /// </summary>
        private void FailUpload(string message)
        {
            ReportError(message);
            _nextUpload = Time.realtimeSinceStartupAsDouble + RETRY_INTERVAL_SECONDS;
            _retryNotBefore = _nextUpload;
            _uploading = false;
        }

        /// <summary>message를 최신 진단으로 보관하고 최초 오류만 Console에 알린다. URL·token·본문은 출력하지 않는다.</summary>
        private void ReportError(string message)
        {
            _lastError = message;
            if (_lastWarnedError == message) return;
            _lastWarnedError = message;
            Debug.LogWarning("전투 로그: " + message, this);
        }

        /// <summary>종료 요청 때 기록자를 한 번 닫고 저장·전송 유예를 시작한다. 유예가 끝나기 전에는 false를 반환해 정상 종료를 보류한다.</summary>
        private bool HandleWantsToQuit()
        {
            if (_quitAllowed) return true;
            if (_quitRequested) return false;
            _quitRequested = true;
            Application.runInBackground = true;
            NotifyQuitRequested();
            RequestFlush();
            StartCoroutine(DrainBeforeQuit());
            return false;
        }

        /// <summary>등록된 기록자를 각각 종료 알림한다. 한 기록자 오류가 다른 기록자의 종료 로그를 막지 않게 한다.</summary>
        private void NotifyQuitRequested()
        {
            if (_quitNotified) return;
            _quitNotified = true;
            Action handlers = QuitRequested;
            if (handlers == null) return;
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception exception) { ReportError("종료 기록자 처리 실패: " + exception.Message); }
            }
        }

        /// <summary>종료 요청으로 저장된 사건과 디스크의 ACK 상태를 최대 35초 확인한다. 전송 완료 또는 제한 시간 뒤 종료를 허용하고 미확인 원본은 보존한다.</summary>
        private IEnumerator DrainBeforeQuit()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + QUIT_UPLOAD_TIMEOUT_SECONDS;
            yield return null;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (_write == null && _writing == null && _buffer.Count == 0 && !_uploading
                    && (_diskDrained || !_allowUpload || _uploadDisabled))
                    break;
                yield return null;
            }
            if (!_diskDrained && _allowUpload && !_uploadDisabled)
                ReportError("종료 전송 제한 시간 초과: 미확인 로그는 로컬에 보존하며 다음 실행에서 재전송합니다.");
            _quitAllowed = true;
            Application.Quit();
        }

        /// <summary>진행 중인 파일 저장 뒤 메모리 버퍼의 마지막 사건을 JSONL에 추가하고 최대 1.5초 기다린다. 저장 작업이 제한 시간 안에 끝나지 않으면 진단 오류를 남긴다.</summary>
        private void PersistPendingEventsOnShutdown()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500);
            if (_write != null)
            {
                Task activeWrite = _write;
                if (!WaitForTaskUntil(activeWrite, deadline))
                {
                    ReportError("종료 로컬 저장 제한 시간을 넘어 미전송 로그를 다음 실행에 보존하지 못했을 수 있습니다.");
                    return;
                }
                if (activeWrite.IsFaulted && _writing != null)
                {
                    _buffer.InsertRange(0, _writing);
                    ReportError("종료 중 로컬 저장 실패: " + activeWrite.Exception.GetBaseException().Message);
                }
                _write = null;
                _writing = null;
            }

            if (_buffer.Count == 0) return;
            string[] pending = _buffer.ToArray();
            _buffer.Clear();
            Task finalWrite = _store.AppendAsync(pending);
            if (!WaitForTaskUntil(finalWrite, deadline))
            {
                _buffer.InsertRange(0, pending);
                ReportError("종료 로컬 저장 제한 시간을 넘어 미전송 로그를 다음 실행에 보존하지 못했을 수 있습니다.");
                return;
            }
            if (finalWrite.IsFaulted)
            {
                _buffer.InsertRange(0, pending);
                ReportError("종료 중 로컬 저장 실패: " + finalWrite.Exception.GetBaseException().Message);
                return;
            }
            if (!string.IsNullOrEmpty(_store.MetadataError)) ReportError(_store.MetadataError);
        }

        /// <summary>task가 deadline 안에 끝났는지 반환한다. 종료 저장 제한을 초과하지 않도록 기다린다.</summary>
        private static bool WaitForTaskUntil(Task task, DateTime deadline)
        {
            int timeoutMilliseconds = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
            return Task.WaitAny(new[] { task }, timeoutMilliseconds) == 0;
        }

#if UNITY_EDITOR
        /// <summary>Editor Play Mode 종료 직전에 기록자를 닫고 마지막 메모리 사건을 로컬 파일에 보존한다.</summary>
        private void HandleEditorPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            NotifyQuitRequested();
            RequestFlush();
            PersistPendingEventsOnShutdown();
        }
#endif

        void OnApplicationPause(bool paused)
        {
            if (paused) RequestFlush();
        }

        void OnApplicationQuit()
        {
            NotifyQuitRequested();
            if (!_quitRequested) RequestFlush();
            _request?.Abort();
        }

        void OnDestroy()
        {
            if (!Application.isEditor) Application.wantsToQuit -= HandleWantsToQuit;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= HandleEditorPlayModeChanged;
#endif
            NotifyQuitRequested();
            _quitting = true;
            _request?.Abort();
            try
            {
                PersistPendingEventsOnShutdown();
            }
            catch (Exception exception) { _lastError = "종료 저장 실패: " + exception.GetBaseException().Message; }
            finally { if (_active == this) _active = null; }
        }

        /// <summary>새 Play 도메인에서 이전 서비스 정적 참조를 제거한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
        }
    }
}
