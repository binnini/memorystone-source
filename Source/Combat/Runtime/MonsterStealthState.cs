using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 은신·특성 알림 협력자(4단계 구조 리팩토링 4-C · 2026-09-04). 옛 <c>CombatState.MonsterStealth.cs</c>의 본문을
    /// <b>무변경</b>으로 옮겼고, 호스트에는 <see cref="IMonsterStealthHost"/>를 통해서만 닿는다. 자기 필드가 없다 —
    /// 은신 상태는 <see cref="MonsterRuntime"/>(StealthRevealTurnsRemaining·AgitationStacks)에 산다.
    /// CombatState의 공개·사설 표면은 위임으로 유지된다(리플렉션 테스트가 사설 이름을 잡으므로 사설 위임도 남긴다).
    /// </summary>
    internal sealed class MonsterStealthState
    {
        private readonly IMonsterStealthHost host;

        public MonsterStealthState(IMonsterStealthHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// 이 몬스터가 <b>은신으로</b> 가려져 있는가. 안개(시야) 판정은 <b>여기 없다</b> — 호출부가
        /// 안개 술어와 <c>&amp;&amp;</c>로 엮는다.
        /// </summary>
        public bool IsMonsterHiddenByStealth(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            var monster = host.Monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterId, StringComparison.Ordinal));
            return IsMonsterHiddenByStealth(monster);
        }

        internal bool IsMonsterHiddenByStealth(MonsterRuntime monster)
        {
            return monster != null
                   && !monster.Combatant.IsDead
                   && monster.StealthRevealTurnsRemaining <= 0
                   && TryGetHiddenTraitSpec(monster, out _);
        }

        internal bool TryGetHiddenTraitSpec(MonsterRuntime monster, out MonsterHiddenTraitSpec spec)
        {
            spec = default;
            return host.TryGetEnemyGrammarEntry(monster, out var entry)
                   && entry.HasHiddenTrait
                   && MonsterHiddenTrait.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out spec, out _);
        }

        /// <summary>
        /// 노출 시작. 저작된 턴 수로 채우고 성장을 0으로 되돌린다 — 「드러나지 않은 턴」이 성장 조건이라
        /// 드러난 순간이 곧 리셋이다.
        /// </summary>
        internal void RevealStealthMonster(MonsterRuntime monster, string sourceRef, string presentationGroupId = "")
        {
            if (monster == null || !TryGetHiddenTraitSpec(monster, out var spec))
            {
                return;
            }

            var wasHidden = monster.StealthRevealTurnsRemaining <= 0;
            if (!wasHidden)
            {
                // 🔴 <b>이미 드러나 있으면 아무것도 하지 않는다</b>(2026-09-05 사용자 확정). 종전에는
                // Math.Max로 카운터를 다시 채워서, 정찰을 겹치거나 계속 때리면 영영 안 숨었다 —
                // 「언제 다시 사라지는가」가 플레이어 손에 달려 있으면 배지의 숫자가 거짓말이 된다.
                // 다시 숨기까지의 시계는 한 번 켜지면 끝까지 제 속도로 간다.
                return;
            }

            monster.StealthRevealTurnsRemaining = spec.RevealTurns;
            if (spec.HasGrowth)
            {
                // 카운터를 직접 쓰지 않는다 — 힘 상태이상까지 함께 걷혀야 배지와 피해가 갈라지지 않는다.
                host.SetMonsterAgitationStacks(monster, 0);
            }

            // 🔴 신호는 <b>보이는 곳에서만</b> 낸다(2026-09-01 #12). 규칙(노출 카운터)은 위에서 이미
            //    섰고, 여기서 걸리는 것은 화면에 뜨는 「들킴!」뿐이다 — 안개 속에서 그 글자가 뜨면
            //    아무것도 안 보이는 칸이 몬스터의 위치를 그대로 흘린다. 게이트는 특성 알림과
            //    <b>같은 술어</b>(IsMonsterCoordVisible)를 쓴다: 두 벌이면 반드시 갈린다.
            if (wasHidden && IsMonsterCoordVisible(monster))
            {
                // 드러나는 순간은 체감의 절반이다 — 규칙층은 신호만 낸다(연출은 표현층 소관).
                host.RaiseEffect(
                    EffectKind.FogReveal,
                    monster.Coord,
                    0,
                    spec.RevealTurns,
                    monster.Id,
                    sourceRef,
                    sourceUnitId: monster.Id,
                    sourceActorKind: "monster",
                    targetActorKind: "monster",
                    presentationGroupId: presentationGroupId);
            }
        }

        /// <summary>
        /// 정찰 반경 안의 은신 몬스터를 드러낸다(Q1 확정 — 정찰에 「숨은 것을 드러내는」 쓸모가 붙는다).
        /// 호출 자리는 <c>TryPlayerScout</c>의 함정 발견 옆이다: 한 장이 "여기 뭐가 있나"를 통째로 답한다.
        /// </summary>
        internal void RevealStealthMonstersInScoutArea(HexCoord center, int radius)
        {
            if (radius < 0)
            {
                return;
            }

            foreach (var monster in host.Monsters)
            {
                if (!monster.Combatant.IsDead && center.DistanceTo(monster.Coord) <= radius)
                {
                    RevealStealthMonster(monster, StealthScoutRevealRef);
                }
            }
        }

        /// <summary>
        /// 특성 발동 알림 한 줄(2026-09-01). 문안은 <see cref="MonsterTraitAnnouncement"/>가 정하고
        /// 여기서는 <b>누구 위에, 지금 띄워도 되는가</b>만 판단한다.
        ///
        /// <para>🔴 <b>안 보이는 몬스터는 알리지 않는다.</b> 특성 알림은 좌표에 뜨는 글자라,
        /// 안개 속·은신 중인 놈이 약오름을 쌓을 때마다 위치가 새어 나간다 — 은신의 값이
        /// 통째로 사라지는 종류의 누수다.</para>
        ///
        /// <para>🔑 예외는 <b>은신 진입</b> 하나(<paramref name="allowWhileHidden"/>): 그 순간은
        /// 「방금까지 보이던 놈이 사라진다」이고, 사라지는 것을 알리는 것이 곧 그 특성의 연출이다.
        /// 그 뒤로는 같은 게이트에 걸려 조용해진다.</para>
        /// </summary>
        internal void RaiseMonsterTraitAnnouncement(
            MonsterRuntime monster, string traitRef, int amount = 0, bool allowWhileHidden = false,
            string presentationGroupId = "")
        {
            if (monster == null || string.IsNullOrEmpty(traitRef) || !IsMonsterCoordVisible(monster))
            {
                return;
            }

            if (!allowWhileHidden && IsMonsterHiddenByStealth(monster))
            {
                return;
            }

            host.RaiseEffect(
                EffectKind.MonsterTraitTriggered,
                monster.Coord,
                0,
                amount,
                monster.Id,
                traitRef,
                sourceUnitId: monster.Id,
                sourceActorKind: "monster",
                targetActorKind: "monster",
                presentationGroupId: presentationGroupId);
        }

        /// <summary>
        /// 이 몬스터가 선 칸이 <b>지금 보이는가</b>. 좌표에 뜨는 글자(특성 알림·노출 신호)는 전부 이
        /// 문을 지난다 — 안 보이는 칸에 글자가 뜨면 그 자체가 위치를 흘리기 때문이다.
        ///
        /// <para>가시성을 알 수 없으면(<c>visibilityRuntime</c> 없음) <b>안 보이는 것으로</b> 친다.
        /// 모르는 쪽으로 기울여야 누수가 안 난다.</para>
        /// </summary>
        internal bool IsMonsterCoordVisible(MonsterRuntime monster)
        {
            return monster != null
                   && host.Visibility != null
                   && host.Visibility.GetVisibility(monster.Coord) == HexCellVisibility.Revealed;
        }

        internal const string StealthAttackRevealRef = "monster.stealth.attack";

        internal const string StealthScoutRevealRef = "monster.stealth.scout";

        /// <summary>
        /// 턴 경계 진행: 노출 카운트다운과 「어둠 먹기」 성장. 적 문법 틱
        /// (<see cref="TickEnemyGrammarPerTurn"/>)과 같은 경계에서 돈다.
        ///
        /// <para>🔴 성장 조건은 <b>「드러나지 않은 턴」</b>이다. 약오름의 조건(감지 중 + 플레이어 농성)은
        /// 정반대고, 맷집의 재장전은 2026-08-20 #11에서 순수 쿨다운이 되어 조건 축이 사라졌다 —
        /// 그래서 조건은 빌려 오지 않고 은신 상태에서 직접 읽는다. 빌려 쓰는 것은 <b>스택 저장·피해
        /// 보너스·세이브</b>(약오름 배관)뿐이다.</para>
        /// </summary>
        internal void TickMonsterStealthPerTurn()
        {
            foreach (var monster in host.Monsters)
            {
                if (monster.Combatant.IsDead || !TryGetHiddenTraitSpec(monster, out var spec))
                {
                    continue;
                }

                if (monster.StealthRevealTurnsRemaining > 0)
                {
                    monster.StealthRevealTurnsRemaining -= 1;
                    if (monster.StealthRevealTurnsRemaining <= 0)
                    {
                        // 다시 숨는 <b>그 턴</b>에만 알린다. 이 분기는 카운터가 0을 찍는 한 번만 지나므로
                        // 숨어 있는 내내 반복될 수 없다 — 「전이만 알린다」가 특성 알림 전체의 규약이다.
                        RaiseMonsterTraitAnnouncement(
                            monster, MonsterTraitAnnouncement.StealthHiddenRef, allowWhileHidden: true);
                    }

                    continue;
                }

                if (spec.HasGrowth)
                {
                    host.SetMonsterAgitationStacks(
                        monster, Math.Min(spec.GrowthMax, monster.AgitationStacks + spec.GrowthPerTurn));
                }
            }
        }

        /// <summary>
        /// 공격이 <b>해소된</b> 시점의 노출(Q1 확정 — 예고 시점이 아니다). 예고 때 드러나면 숨어서 치는
        /// 맛이 사라지고, 플레이어는 맞기 전에 이미 다 보게 된다.
        /// </summary>
        /// <summary>
        /// 맞아서 드러난다(2026-09-05 사용자 확정). 부적·장판·함정·반사 어느 경로든
        /// <see cref="CombatState.DamageMonster"/> 관문 하나를 지나므로 여기 한 줄이면 전부 걸린다.
        /// 문안은 공격으로 드러난 것과 같은 「들킴!」이다 — 플레이어가 읽을 사실이 하나이기 때문이다.
        /// </summary>
        internal void RevealStealthMonsterOnHit(MonsterRuntime monster)
        {
            RevealStealthMonster(monster, StealthAttackRevealRef);
        }

        internal void RevealStealthMonsterAfterAttack(MonsterRuntime monster, string presentationGroupId = "")
        {
            RevealStealthMonster(monster, StealthAttackRevealRef, presentationGroupId);
        }

        /// <summary>
        /// 「달아나기」(야광귀) — 패턴 저작(<c>selfTeleportRadius</c>)만으로 도는 순간이동. 은신과 무관하므로
        /// 특성이 없는 몬스터도 쓴다. 훔치고 사라지는 한 수가 이 한 줄로 성립한다.
        /// </summary>
        internal bool ResolvePatternSelfTeleport(
            MonsterRuntime monster, MonsterAttackPattern pattern, out HexCoord from, out HexCoord to)
        {
            from = default;
            to = default;
            // MonsterAttackPattern은 구조체다 — null 비교가 아니라 저작값으로 게이트한다.
            if (monster == null || monster.Combatant.IsDead || pattern.SelfTeleportRadius <= 0)
            {
                return false;
            }

            var before = monster.Coord;
            TeleportMonsterDeterministically(monster, pattern.Id, pattern.SelfTeleportRadius);
            if (monster.Coord == before)
            {
                return false;
            }

            from = before;
            to = monster.Coord;
            return true;
        }

        /// <summary>
        /// 목적지 선정의 <b>단일 지점</b>(구 은신 흩어지기 A039도 여길 지났다 — 2026-09-04 패턴 삭제 후
        /// 남은 사용자는 패턴 순간이동뿐이지만, 새 순간이동 문법이 생기면 반드시 이 함수를 태운다).
        ///
        /// <para>🔴 <b>무시드 RNG 금지</b> — 세이브가 full-snapshot이라 저장·복원 뒤 다른 칸으로 사라지면
        /// "어디로 갔나"를 되짚을 수 없다. 순수 함수(몬스터 id · 패턴 id · 턴 번호)로 뽑는다.</para>
        /// </summary>
        internal void TeleportMonsterDeterministically(MonsterRuntime monster, string patternId, int radius)
        {
            host.UpdateOccupancy();
            var candidates = host.CollectTeleportCandidates(monster.Coord, radius);
            if (candidates.Count == 0)
            {
                return;
            }

            var ordered = candidates
                .OrderBy(coord => monster.Coord.DistanceTo(coord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();
            var seed = CombatDeterministicSeed.Mix($"{monster.Id}|{patternId}", host.OverallTurnNumber);
            var destination = ordered[(int)((uint)seed % (uint)ordered.Count)];

            monster.Coord = destination;
            host.UpdateOccupancy();
        }
    }
}
