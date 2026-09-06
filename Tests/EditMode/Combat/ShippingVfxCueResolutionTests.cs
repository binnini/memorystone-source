#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 트랙 ② 효과 발신 키 은퇴(2026-09-06)의 감시자. 규칙층은 카드가 직접 올리는 효과의 sourceRef로
    /// <b>카드 id</b>를 보내고, 카드가 떠난 뒤 오르는 효과(장판 틱·지연 부여·피해 면역 알림)만
    /// <b>효과 종류 키</b>(<see cref="CardEffectRefs"/>)를 쓴다. 큐 CSV의 sourceRef는 그 둘 중 하나여야
    /// 하고, 옛 behaviorId 문자열(<c>attack.damage</c>·<c>defend.zero_then_double</c> …)이 다시 들어오면
    /// 큐가 조용히 죽는다 — P0 조사에서 CVA03H가 그렇게 죽어 있었다.
    /// 저작값 핀이 아니다: 어느 행이 어느 키를 쓰는지는 묻지 않고, 「규칙층이 실제로 내는 형태의 이벤트가
    /// 그 행을 맞추는가」만 묻는다.
    /// </summary>
    [Category("ShippingData")]
    public sealed class ShippingVfxCueResolutionTests
    {
        /// <summary>
        /// 카드가 아닌 발신자가 올리는 효과 종류 키. 여기 없는 점 구분 키가 큐 CSV에 나타나면 그 행은
        /// 어떤 이벤트도 맞출 수 없다(카드 발신은 전부 카드 id이므로).
        /// </summary>
        private static readonly HashSet<string> EffectKindKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            CardEffectRefs.FieldPlacement,
            CardEffectRefs.FieldDamage,
            CardEffectRefs.FieldHeal,
            CardEffectRefs.FieldLifesteal,
            CardEffectRefs.FieldImmobilizeFlashbang,
            CardEffectRefs.FieldFogReveal,
            CardEffectRefs.DefendDamageImmunity,
            CardEffectRefs.DefendHalfReflect,
            CardEffectRefs.MoveDeferredMomentumApply,
            CardEffectRefs.SelfDelayedImmobilizeApply,
        };

        private static EffectVfxCatalog LoadShippingCatalog()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null, "출하 VFX 카탈로그를 못 읽었다 — 게이트가 빈 채로 통과하고 있다.");
            return catalog;
        }

        private static IReadOnlyList<CombatCardVfxCueDefinition> LoadCueRows()
        {
            var rows = CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv);
            Assert.That(rows, Is.Not.Empty, "combat_card_vfx_cues.csv produced no rows.");
            return rows;
        }

        [Test]
        public void EveryCueSourceRefIsEitherTheCardIdOrAnEffectKindKey()
        {
            var offenders = LoadCueRows()
                .Where(row => !string.Equals(row.SourceRef, row.CardId, StringComparison.Ordinal)
                              && !EffectKindKeys.Contains(row.SourceRef))
                .Select(row => $"{row.CueId}: {row.SourceRef}")
                .ToArray();

            Assert.That(
                offenders, Is.Empty,
                "큐 sourceRef가 카드 id도, 효과 종류 키도 아니다 — 규칙층이 그런 키를 보내지 않으므로 이 행은 죽은 행이다: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// 규칙층이 내는 형태의 이벤트(sourceRef = 행의 키, sourceCardId = 그 카드)로 카탈로그를 조회하면
        /// <b>그 행</b>이 나와야 한다. 같은 키를 여러 카드가 공유하는 장판 행(CVF01M ↔ CVF05M)은
        /// sourceCardId 티어가 갈라 주므로 여기서도 정확히 자기 행이어야 한다.
        /// </summary>
        [Test]
        public void EveryCueIsReachedByTheEventTheRulesLayerActuallyRaises()
        {
            var catalog = LoadShippingCatalog();
            var unreachable = new List<string>();

            foreach (var row in LoadCueRows())
            {
                var resultEvent = new EffectResultEvent(
                    row.EffectKind,
                    targetUnitId: ToTargetUnitId(row.TargetFilter),
                    sourceRef: row.SourceRef,
                    sourceCardId: row.CardId);

                if (!catalog.TryResolve(resultEvent, out var entry) || !string.Equals(entry.CueId, row.CueId, StringComparison.Ordinal))
                {
                    unreachable.Add($"{row.CueId} ({row.EffectKind} {row.SourceRef} / {row.CardId}) -> {(entry != null ? entry.CueId : "none")}");
                }
            }

            Assert.That(unreachable, Is.Empty, "규칙층 이벤트가 맞추지 못하는 큐 행: " + string.Join(", ", unreachable));
        }

        private static string ToTargetUnitId(string targetFilter)
        {
            if (string.Equals(targetFilter, "Player", StringComparison.OrdinalIgnoreCase))
            {
                return "player";
            }

            if (string.Equals(targetFilter, "Field", StringComparison.OrdinalIgnoreCase))
            {
                return "field";
            }

            return "monster";
        }
    }
}
#endif
