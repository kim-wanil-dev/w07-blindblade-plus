using System;
using System.Globalization;

using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
    /// <summary>Unity 객체 참조 없이 공통 계약 값을 저장용 JSON으로 변환한다.</summary>
    internal static class TelemetryJson
    {
        internal const string SCHEMA_VERSION = "0.2.0";
        private const int KOREA_STANDARD_TIME_OFFSET_HOURS = 9;

        /// <summary>stamp의 세 공통 시계를 JSON 숫자로 복사하여 반환한다.</summary>
        internal static JObject Timestamp(CombatTimestamp stamp)
        {
            return new JObject { ["realtimeSeconds"] = stamp.RealtimeSeconds,
                ["gameplaySeconds"] = stamp.GameplaySeconds, ["activeUnscaledSeconds"] = stamp.ActiveUnscaledSeconds };
        }

        /// <summary>
        /// 기준 KST 시각에 stamp와 기준 실제 경과 초의 차이를 더한 ISO 8601 문자열을 반환한다.
        /// referenceTimeKst와 referenceRealtimeSeconds를 사용하며 +09:00 오프셋을 포함한다.
        /// </summary>
        internal static string KoreanWallClock(DateTimeOffset referenceTimeKst,
            double referenceRealtimeSeconds, CombatTimestamp stamp)
        {
            DateTimeOffset eventTime = referenceTimeKst.AddSeconds(stamp.RealtimeSeconds - referenceRealtimeSeconds);
            return FormatKoreanTime(eventTime);
        }

        /// <summary>
        /// 현재 UTC 시각을 +09:00으로 변환해 반환한다.
        /// 컴퓨터의 로컬 시간대 설정과 관계없이 한국 표준시 오프셋을 사용한다.
        /// </summary>
        internal static DateTimeOffset CurrentKoreanTime()
        {
            return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(KOREA_STANDARD_TIME_OFFSET_HOURS));
        }

        /// <summary>
        /// value를 ISO 8601 한국 표준시 문자열로 반환한다.
        /// value의 +09:00 오프셋을 보존하고 밀리초까지 출력한다.
        /// </summary>
        internal static string FormatKoreanTime(DateTimeOffset value)
        {
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
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
