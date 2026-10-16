using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Networking;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlindBlade.Telemetry
{
    /// <summary>씬을 넘어 원본 저장·디스크 기반 재전송을 유지한다. 전투 시스템에는 결과를 돌려주지 않는다.</summary>
    public sealed class CombatTelemetryService : MonoBehaviour
    {
        private const int MAXIMUM_BATCH_BYTES = 196608;
        private const float QUIT_UPLOAD_GRACE_SECONDS = 20f;
        private static CombatTelemetryService _active;

        private readonly List<string> _buffer = new List<string>();
        private TelemetryDiskStore _store;
        private Task _write;
        private string[] _writing;
        private UnityWebRequest _request;
        private int _bufferLimit;
        private int _batchLimit;
        private int _timeout;
        private float _flushInterval;
        private float _uploadInterval;
        private float _maximumRetry;
        private double _nextFlush;
        private double _nextUpload;
        private bool _allowUpload;
        private bool _uploading;
        private bool _quitting;
        private bool _quitRequested;
        private bool _quitReady;
        private bool _uploadIdle;
        private bool _reportedUploadSuccess;
        private string _lastError;
        private string _lastWarnedError;
        private int _dropped;
        private int _uploaded;
        private int _failures;
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

        /// <summary>최초 recorder의 설정으로 영속 서비스를 만들거나 기존 서비스를 반환한다. 설정은 재도전 때 덮어쓰지 않는다.</summary>
        public static CombatTelemetryService GetOrCreate(int bufferLimit, float flushInterval, int maximumMegabytes,
            bool allowUpload, float uploadInterval, int batchLimit, int timeout, float maximumRetry)
        {
            if (_active != null) return _active;
            GameObject host = new GameObject("CombatTelemetryService");
            DontDestroyOnLoad(host);
            _active = host.AddComponent<CombatTelemetryService>();
            _active._store = new TelemetryDiskStore(Path.Combine(Application.persistentDataPath, "CombatTelemetry"),
                Math.Max(1, maximumMegabytes) * 1024L * 1024L);
            _active._bufferLimit = Math.Max(64, bufferLimit);
            _active._flushInterval = Mathf.Max(0.2f, flushInterval);
            _active._uploadInterval = Mathf.Max(5f, uploadInterval);
            _active._batchLimit = Mathf.Clamp(batchLimit, 1, 100);
            _active._timeout = Mathf.Clamp(timeout, 5, 120);
            _active._maximumRetry = Mathf.Max(30f, maximumRetry);
            _active._allowUpload = allowUpload;
            if (!Application.isEditor) Application.wantsToQuit += _active.HandleWantsToQuit;
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

        /// <summary>record를 즉시 문자열 스냅샷으로 버퍼에 넣는다. 한도 초과면 거부하고 누락 수를 증가시킨다.</summary>
        internal void Enqueue(JObject record)
        {
            if (_quitting) return;
            if (BufferedEvents >= _bufferLimit)
            {
                _dropped++;
                ReportError("로그 메모리 한도를 넘어 사건이 누락되었습니다.");
                return;
            }
            _buffer.Add(record.ToString(Formatting.None));
        }

        /// <summary>다음 Update에서 저장·전송을 앞당긴다. 종료 확인 응답을 기다리며 게임을 멈추지 않는다.</summary>
        public void RequestFlush()
        {
            _nextFlush = 0d;
            _nextUpload = 0d;
        }

        void Update()
        {
            ObserveWrite();
            double now = Time.realtimeSinceStartupAsDouble;
            if (_write == null && _buffer.Count > 0 && now >= _nextFlush)
            {
                _writing = _buffer.ToArray();
                _buffer.Clear();
                _write = _store.AppendAsync(_writing);
                _nextFlush = now + _flushInterval;
            }
            if (_allowUpload && !_uploading && now >= _nextUpload)
                StartCoroutine(Upload());
        }

        /// <summary>완료된 저장 작업의 오류를 격리한다. 부분 저장 실패는 중복 가능성을 알리고 해당 버퍼를 누락 집계한다.</summary>
        private void ObserveWrite()
        {
            if (_write == null || !_write.IsCompleted) return;
            if (_write.IsFaulted)
            {
                _dropped += _writing.Length;
                ReportError("로컬 저장 실패(일부 저장 가능): " + _write.Exception.GetBaseException().Message);
            }
            _write = null;
            _writing = null;
            if (_quitRequested)
            {
                _uploadIdle = false;
                _nextUpload = 0d;
            }
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
                if (configuration.Value<bool?>("enabled") != true) { _uploading = false; yield break; }
                if (!Uri.TryCreate(configuration.Value<string>("endpoint"), UriKind.Absolute, out Uri endpoint)
                    || endpoint.Scheme != Uri.UriSchemeHttps || (configuration.Value<string>("token")?.Length ?? 0) < 32)
                    throw new InvalidDataException("전송 설정의 HTTPS endpoint와 32자 이상 token을 설정하세요. 설정 출처: " + _configurationSource);
            }
            catch (Exception exception)
            {
                ReportError("전송 설정 오류: " + exception.Message);
                _uploading = false;
                yield break;
            }

            string destination;
            using (SHA256 algorithm = SHA256.Create())
                destination = BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(configuration.Value<string>("endpoint"))))
                    .Replace("-", "").Substring(0, 32);
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
                _uploadIdle = true;
                _uploading = false;
                yield break;
            }
            _uploadIdle = false;
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
            _failures = 0;
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

        /// <summary>message를 진단에 남기고 지수 지연 후 재시도한다. 원본과 확인 커서는 변경하지 않는다.</summary>
        private void FailUpload(string message)
        {
            ReportError(message);
            _failures = Math.Min(_failures + 1, 10);
            _nextUpload = Time.realtimeSinceStartupAsDouble
                + Math.Min(_maximumRetry, _uploadInterval * Math.Pow(2d, _failures));
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

        /// <summary>플레이어 종료 요청 때 기록자를 닫고 로컬 로그 전송을 기다린다. 전송 완료 또는 제한 시간 경과 후 종료를 허용한다.</summary>
        private bool HandleWantsToQuit()
        {
            if (_quitReady || !_allowUpload) return true;
            if (_quitRequested) return false;
            _quitRequested = true;
            _uploadIdle = false;
            QuitRequested?.Invoke();
            RequestFlush();
            StartCoroutine(WaitForUploadBeforeQuit());
            return false;
        }

        /// <summary>남은 로그가 확인될 때까지 최대 제한 시간만 기다린다. 미확인 로그는 디스크에 남겨 다음 실행에서 재전송한다.</summary>
        private IEnumerator WaitForUploadBeforeQuit()
        {
            double deadline = Time.realtimeSinceStartupAsDouble + QUIT_UPLOAD_GRACE_SECONDS;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (_uploadIdle && !_uploading && _write == null && _buffer.Count == 0) break;
                yield return null;
            }
            _quitReady = true;
            Application.Quit();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) RequestFlush();
        }

        void OnApplicationQuit()
        {
            _request?.Abort();
        }

        void OnDestroy()
        {
            if (!Application.isEditor) Application.wantsToQuit -= HandleWantsToQuit;
            _quitting = true;
            _request?.Abort();
            // 종료 시 네트워크를 기다리지 않고 남은 원본 저장만 짧게 기다린다.
            try
            {
                if (_write != null && !_write.Wait(1500)) return;
                ObserveWrite();
                if (_buffer.Count > 0) _store.AppendAsync(_buffer.ToArray()).Wait(1500);
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
