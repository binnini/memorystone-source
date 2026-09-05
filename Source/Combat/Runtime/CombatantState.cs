using System;

namespace SeoulPlayup.Combat.Runtime
{
    [Serializable]
    public sealed class CombatantState
    {
        public CombatantState(string id, int maxHp)
        {
            Id = string.IsNullOrWhiteSpace(id) ? "unit" : id;
            MaxHp = Math.Max(1, maxHp);
            Hp = MaxHp;
        }

        public string Id { get; }
        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public int Block { get; private set; }
        public bool IsDead => Hp <= 0;

        public void AddBlock(int amount)
        {
            Block += Math.Max(0, amount);
        }

        public void ClearBlock()
        {
            Block = 0;
        }

        /// <summary>
        /// 최대 체력을 올리고 <b>현재 체력도 같은 양만큼</b> 올린다(유물 MaxHpBonus). 상점이 전장 안에 있어
        /// 전투 도중 최대 체력이 오를 수 있으므로 MaxHp는 생성자 고정값이 아니다. 현재 체력을 함께 올리지
        /// 않으면 위급할 때 산 체력 유물이 그 판에서는 아무 일도 하지 않는다.
        /// 감소는 지원하지 않는다 — 음수/0은 무시한다.
        /// </summary>
        public void IncreaseMaxHp(int amount)
        {
            var gain = Math.Max(0, amount);
            if (gain == 0)
            {
                return;
            }

            MaxHp += gain;
            if (IsDead)
            {
                // 부활 규칙은 이 게임에 없다. 사망자의 Hp를 올리면 최대 체력 유물이 부활 수단이 된다.
                return;
            }

            Hp = Math.Min(MaxHp, Hp + gain);
        }

        /// <summary>
        /// 최대 체력을 깎는다(마이너스 통장 MaxHpPenalty, T2 페이즈 B). 하한 1 — 획득의 대가가 즉사
        /// 수단이 되면 안 된다. 현재 체력이 새 최대치를 넘으면 함께 깎이되 역시 1 밑으로는 안 내려간다.
        /// </summary>
        public void ReduceMaxHp(int amount)
        {
            var loss = Math.Max(0, amount);
            if (loss == 0 || IsDead)
            {
                return;
            }

            MaxHp = Math.Max(1, MaxHp - loss);
            Hp = Math.Max(1, Math.Min(Hp, MaxHp));
        }

        public int ApplyDamage(int damage)
        {
            var remaining = Math.Max(0, damage);
            var absorbed = Math.Min(Block, remaining);
            Block -= absorbed;
            remaining -= absorbed;
            var previousHp = Hp;
            Hp = Math.Max(0, Hp - remaining);
            return previousHp - Hp;
        }

        public int Heal(int amount)
        {
            var clampedAmount = Math.Max(0, amount);
            var previousHp = Hp;
            Hp = Math.Min(MaxHp, Hp + clampedAmount);
            return Hp - previousHp;
        }

        public int HealUncapped(int amount)
        {
            var clampedAmount = Math.Max(0, amount);
            Hp += clampedAmount;
            return clampedAmount;
        }

        internal void RestoreVitals(int hp, int block)
        {
            Hp = Math.Max(0, hp);
            Block = Math.Max(0, block);
        }
    }
}
