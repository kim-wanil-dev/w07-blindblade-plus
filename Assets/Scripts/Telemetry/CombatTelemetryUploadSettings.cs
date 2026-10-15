using System.IO;

using UnityEngine;

using Newtonsoft.Json.Linq;

namespace BlindBlade.Telemetry
{
    /// <summary>로컬 재정의 파일이 없는 PC에서 사용할 배포용 전송 기본값을 보관한다.</summary>
    [CreateAssetMenu(fileName = "CombatTelemetryUploadSettings", menuName = "BlindBlade/로그/배포 전송 기본 설정")]
    public sealed class CombatTelemetryUploadSettings : ScriptableObject
    {
        private const string RESOURCE_PATH = "Telemetry/CombatTelemetryUploadSettings";

        [Header("배포 기본 전송")]
        [SerializeField] private bool _enabled = true;
        [SerializeField] private string _endpoint;
        [SerializeField, HideInInspector] private string _uploadToken;

        /// <summary>Resources의 배포 설정을 읽어 전송용 값 스냅샷을 반환한다. 설정이 없으면 누락을 알리는 예외를 발생시킨다.</summary>
        public static JObject LoadConfiguration()
        {
            CombatTelemetryUploadSettings settings = Resources.Load<CombatTelemetryUploadSettings>(RESOURCE_PATH);
            if (settings == null)
                throw new FileNotFoundException("빌드 기본 전송 설정이 없습니다. 로그 메뉴에서 현재 로컬 설정을 빌드 기본값으로 저장하세요.");
            return settings.CreateConfiguration();
        }

        /// <summary>직렬화된 enabled·endpoint·공유 token을 새 JSON 객체로 복사한다. 에셋이나 로컬 설정을 변경하지 않는다.</summary>
        private JObject CreateConfiguration()
        {
            return new JObject { ["enabled"] = _enabled, ["endpoint"] = _endpoint, ["token"] = _uploadToken };
        }
    }
}
