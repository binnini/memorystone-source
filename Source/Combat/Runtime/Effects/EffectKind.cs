namespace SeoulPlayup.Combat.Runtime
{
    public enum EffectKind
    {
        Damage,
        Block,
        Heal,
        FogReveal,
        Push,
        ReflectDamage,
        StatusEffectApplied,

        /// <summary>
        /// Forced displacement of a unit by an external source (monster collision, directional knockback
        /// card/attack). Distinct from <see cref="Push"/>, which the codebase uses for ordinary self-movement
        /// (move cards). Appended last to keep existing serialized enum values stable.
        /// </summary>
        Knockback,

        /// <summary>
        /// Incoming damage that was fully absorbed by the target's Block (no HP lost). Distinct from
        /// <see cref="Block"/>, which signals gaining block ("방어 +N"); this signals a hit being negated
        /// ("방어!"). Appended last to keep existing serialized enum values stable.
        /// </summary>
        DamageBlocked,

        /// <summary>
        /// A status effect has worn off / been removed ("상태 해제"). Lets the presentation layer surface the
        /// expiry as its own beat distinct from application. Appended last to keep serialized values stable.
        /// </summary>
        StatusEffectExpired,

        /// <summary>
        /// A telegraphed attack was cancelled before resolving (e.g. the attacker was controlled into 대기), so
        /// the presentation layer can show a cancel cue instead of an impact. Appended last to keep serialized
        /// enum values stable.
        /// </summary>
        AttackCancelled,

        /// <summary>
        /// A monster committed to an attack presentation, but the player was no longer inside the resolved
        /// attack area when it fired. Presentation-only feedback ("빗맞음"). Appended last to keep serialized
        /// enum values stable.
        /// </summary>
        AttackMissed,

        /// <summary>
        /// 수호(T2 페이즈 C)가 해로운 상태이상 부여를 무효화했다("{상태} 무효!" — 사용자 확정 문안).
        /// StatusKind에 무효화된 종류가 실린다. 부여 이벤트(StatusEffectApplied)는 반환값 게이트로
        /// 삼켜지고 이 이벤트가 대신 나간다. Appended last to keep serialized enum values stable.
        /// </summary>
        StatusNegated,

        /// <summary>
        /// 발견된 함정이 카드로 해체됐다("해체!" — 2026-08-20 #5, S05 돌 다리 두드리기). 함정 좌표에
        /// 텍스트만 띄우는 연출 전용 kind(AttackMissed·StatusNegated와 같은 무VFX 계열) — 실제 소진은
        /// 규칙 쪽 consumedTrapIds가 이미 끝냈다. Appended last to keep serialized enum values stable.
        /// </summary>
        TrapDisarmed,

        /// <summary>
        /// 몬스터 <b>특성</b>이 발동했다(2026-09-01 사용자 확정 — 약오름·맷집·견고·뒤끝·은신).
        /// 문안은 <see cref="MonsterTraitAnnouncement"/>가 source ref로 정하고, 이 kind 자체는
        /// 「특성이 지금 일했다」만 나른다. TrapDisarmed·AttackMissed와 같은 <b>무VFX 텍스트 전용</b>
        /// 계열이다 — 특성은 이미 배지로 상시 보이고, 여기서 필요한 것은 <b>언제</b>뿐이라
        /// 파티클을 얹으면 매 턴 화면이 시끄러워진다.
        ///
        /// <para>🔑 은신 <b>해제</b>만 이 kind를 쓰지 않는다 — 노출은 이미 FogReveal로 나가고
        /// 전용 VFX가 붙어 있어, 새 kind 대신 그 이벤트의 문안만 갈아 끼웠다.</para>
        ///
        /// Appended last to keep serialized enum values stable.
        /// </summary>
        MonsterTraitTriggered,

        /// <summary>
        /// 상태 카드(저주)가 이번 전투의 뽑을 더미에 섞였다(2026-09-01 #9).
        /// <c>SourceCardId</c>에 섞인 카드의 id가, <c>SourceRef</c>에 넣은 쪽의 ref가 실린다.
        ///
        /// <para>🔴 <b>종전에는 아무 신호도 없었다.</b> <c>TryInjectStatusCard</c>가 조용히 덱만
        /// 오염시켜서, 플레이어는 <b>몇 턴 뒤 그 카드를 뽑고 나서야</b> 무슨 일이 있었는지 알았다 —
        /// 원인과 결과가 화면에서 이어지지 않는다.</para>
        ///
        /// <para>🔑 이 kind가 나르는 것은 「지금·무엇이」뿐이고, 카드를 보여주고 더미로 빨아들이는
        /// 연출은 표현층이 <b>전리품 비행과 같은 통로</b>로 만든다. 그림이 카드 스냅샷이라
        /// 파티클 카탈로그에는 엔트리가 없다(TrapDisarmed 계열과 같은 무VFX kind).</para>
        ///
        /// Appended last to keep serialized enum values stable.
        /// </summary>
        StatusCardInjected
    }
}
