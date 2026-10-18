using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
    /// <summary>전투 생산자가 확정한 공격 수명과 접촉을 분석기에 전달한다. 게임 판정은 변경하지 않는다.</summary>
    public static class CombatTelemetrySignals
    {
        public static event Action<string, int, JObject> Raised;

        /// <summary>패턴과 시작·조준 위치로 공격 ID를 발급하고 전조 시작을 알린다. 반환 ID는 피해 문맥까지 유지한다.</summary>
        public static int Begin(string pattern, string kind, Vector3 origin, Vector3 aim)
        {
            CombatSessionRuntime runtime = CombatSessionRuntime.Active;
            int id = runtime != null && runtime.IsInitialized ? runtime.AllocateAttackId() : 0;
            Raised?.Invoke("enemy_attack_started", id, new JObject {
                ["patternId"] = pattern, ["kind"] = kind,
                ["origin"] = TelemetryJson.Position(origin), ["aim"] = TelemetryJson.Position(aim) });
            return id;
        }

        /// <summary>boss 공격 ID와 부모 패턴 메타데이터를 발급해 전조 시작을 알린다.</summary>
        public static int BeginBossAttack(string pattern, string kind, Vector3 origin, Vector3 aim,
            string parentPatternId, string patternInstanceId, string attackerActor)
        {
            CombatSessionRuntime runtime = CombatSessionRuntime.Active;
            int id = runtime != null && runtime.IsInitialized ? runtime.AllocateAttackId() : 0;
            Raised?.Invoke("enemy_attack_started", id, new JObject {
                ["patternId"] = pattern, ["kind"] = kind,
                ["origin"] = TelemetryJson.Position(origin), ["aim"] = TelemetryJson.Position(aim),
                ["parentPatternId"] = parentPatternId, ["patternInstanceId"] = patternInstanceId,
                ["attackerActor"] = attackerActor });
            return id;
        }

        /// <summary>분석 가능한 상태 변화 eventType과 필드를 Recorder에 전달한다.</summary>
        public static void Event(string eventType, JObject payload)
        {
            Raised?.Invoke(eventType, 0, payload ?? new JObject());
        }

        /// <summary>대상 이벤트의 필드와 대상 좌표를 JSON으로 만들어 Recorder에 전달한다.</summary>
        public static void Event(string eventType, IEnumerable<KeyValuePair<string, JToken>> fields,
            Vector3 position)
        {
            JObject payload = new JObject { ["position"] = TelemetryJson.Position(position) };
            foreach (KeyValuePair<string, JToken> field in fields)
                payload[field.Key] = field.Value?.DeepClone();
            Event(eventType, payload);
        }

        /// <summary>id의 실제 공격 실행과 발사 원점을 알린다. 전조 취소와 실행된 공격의 분모를 구분한다.</summary>
        public static void Commit(int id, Vector3 origin)
        {
            Raised?.Invoke("enemy_attack_committed", id, new JObject { ["origin"] = TelemetryJson.Position(origin) });
        }

        /// <summary>id의 비접촉 종료 사유와 끝점을 알린다. 이미 접촉한 공격의 결과는 분석기가 유지한다.</summary>
        public static void End(int id, string outcome, Vector3 endpoint)
        {
            Raised?.Invoke("enemy_attack_resolved", id, new JObject {
                ["outcome"] = outcome, ["position"] = TelemetryJson.Position(endpoint) });
        }

        /// <summary>실제 context와 방어 resolution, 회피 여부를 전달한다. 피해 및 접촉 결과를 한 번 기록한다.</summary>
        public static void Contact(DamageContext context, DefenseResolution resolution, bool dodging)
        {
            Raised?.Invoke("enemy_attack_contact", context.AttackId, new JObject {
                ["patternId"] = context.PatternId, ["kind"] = context.Kind.ToString(),
                ["outcome"] = dodging ? "Dodge" : resolution.Outcome.ToString(),
                ["defenseOutcome"] = resolution.Outcome.ToString(), ["inputId"] = resolution.GuardInputId,
                ["appliedDamage"] = resolution.AppliedDamage, ["hitId"] = context.HitId,
                ["position"] = TelemetryJson.Position(context.Point) });
        }

        /// <summary>stage의 시작 또는 complete 완료를 알리고 기록 구간을 전환한다.</summary>
        public static void Stage(string stage, bool complete = false)
        {
            Raised?.Invoke(complete ? "stage_complete" : "stage_change", 0, new JObject { ["stageId"] = stage });
        }
    }
}
