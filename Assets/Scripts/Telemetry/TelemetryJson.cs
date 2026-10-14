using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
    /// <summary>Unity 객체 참조 없이 공통 계약 값을 저장용 JSON으로 변환한다.</summary>
    internal static class TelemetryJson
    {
        internal const string SCHEMA_VERSION = "0.2.0";

        /// <summary>stamp의 세 공통 시계를 JSON 숫자로 복사하여 반환한다.</summary>
        internal static JObject Timestamp(CombatTimestamp stamp)
        {
            return new JObject { ["realtimeSeconds"] = stamp.RealtimeSeconds,
                ["gameplaySeconds"] = stamp.GameplaySeconds, ["activeUnscaledSeconds"] = stamp.ActiveUnscaledSeconds };
        }

        /// <summary>position의 좌표만 복사하여 직렬화 가능한 JSON 객체를 반환한다.</summary>
        internal static JObject Position(Vector3 position)
        {
            return new JObject { ["x"] = position.x, ["y"] = position.y, ["z"] = position.z };
        }

        /// <summary>ids의 명시적 연결 값을 복사한다. 0은 미부여 값이며 임의 연결을 만들지 않는다.</summary>
        internal static JObject Correlation(CombatCorrelationIds ids)
        {
            return new JObject { ["inputId"] = ids.InputId, ["actionId"] = ids.ActionId,
                ["attackId"] = ids.AttackId, ["hitId"] = ids.HitId,
                ["projectileId"] = ids.ProjectileId, ["dodgeId"] = ids.DodgeId };
        }
    }
}
