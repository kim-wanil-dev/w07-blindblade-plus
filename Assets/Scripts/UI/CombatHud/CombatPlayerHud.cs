using UnityEngine;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>A의 HP·스태미너 원본을 초기 조회하고 변경 사건으로 게이지에 전달한다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CombatPlayerHud : MonoBehaviour
    {
        [Header("원본 연결")]
        [SerializeField] private PlayerHealth _health;
        [SerializeField, Tooltip("IPlayerStaminaReadModel을 구현한 A 컴포넌트. 비우면 플레이어에서 한 번 찾습니다.")]
        private MonoBehaviour _staminaSource;
        private PlayerHealth _boundHealth;
        private IPlayerStaminaReadModel _boundStamina;

        [Header("게이지 연결")]
        [SerializeField] private CombatResourceGauge _healthGauge;
        [SerializeField] private CombatResourceGauge _staminaGauge;

        void OnEnable()
        {
            BindSources(_health, _staminaSource);
        }

        void Update()
        {
            if (_boundHealth == null && Game.IsInitialized && Game.Player != null)
                BindSources(Game.Player.GetComponent<PlayerHealth>(), _staminaSource);
        }

        void OnDisable()
        {
            Unsubscribe();
            _healthGauge.SetUnavailable();
            _staminaGauge.SetUnavailable();
        }

        /// <summary>health와 선택적 staminaSource에 구독하고 현재 값을 즉시 표시한다. 공급자가 없으면 미연결 상태로 둔다.</summary>
        public void BindSources(PlayerHealth health, MonoBehaviour staminaSource)
        {
            Unsubscribe();
            _health = health;
            _staminaSource = staminaSource;
            _boundHealth = health;
            _boundStamina = staminaSource as IPlayerStaminaReadModel;
            if (_boundStamina == null && staminaSource == null && health != null)
            {
                foreach (MonoBehaviour component in health.GetComponents<MonoBehaviour>())
                {
                    if (component is IPlayerStaminaReadModel readModel)
                    {
                        _boundStamina = readModel;
                        break;
                    }
                }
            }

            if (_boundHealth != null)
            {
                _boundHealth.HealthChanged += HandleHealthChanged;
                _healthGauge.SetValue(_boundHealth.CurrentHealth, _boundHealth.MaxHealth, true);
            }
            else
                _healthGauge.SetUnavailable();

            if (_boundStamina != null)
            {
                _boundStamina.Changed += HandleStaminaChanged;
                StaminaSnapshot snapshot = _boundStamina.Current;
                _staminaGauge.SetValue(snapshot.Current, snapshot.Maximum, true);
            }
            else
                _staminaGauge.SetUnavailable();
        }

        /// <summary>입력 없이 이 HUD가 등록한 HP·스태미너 처리기만 해제하고 연결 상태를 비운다.</summary>
        private void Unsubscribe()
        {
            if (_boundHealth != null)
                _boundHealth.HealthChanged -= HandleHealthChanged;
            if (_boundStamina != null)
                _boundStamina.Changed -= HandleStaminaChanged;
            _boundHealth = null;
            _boundStamina = null;
        }

        /// <summary>current와 maximum으로 HP 게이지 목표를 갱신한다. 전투 상태는 변경하지 않는다.</summary>
        private void HandleHealthChanged(float current, float maximum)
        {
            _healthGauge.SetValue(current, maximum);
        }

        /// <summary>snapshot의 실제 스태미너 값으로 게이지 목표를 갱신한다. 소모·회복 규칙은 실행하지 않는다.</summary>
        private void HandleStaminaChanged(StaminaSnapshot snapshot)
        {
            _staminaGauge.SetValue(snapshot.Current, snapshot.Maximum);
        }
    }
}
