using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 보스 기물(철조각) 연출 sink. 세 비트를 연주한다:
    /// <list type="number">
    /// <item><b>살포 캐스트</b> — 전조(경고 SFX + 짧은 예열) → 카메라 흔들림 + 보스 공격 애니메이션 +
    ///       캐스트 VFX/SFX. 살포한 턴에는 보스가 일반 공격을 하지 않으므로 이것이 그 턴 보스 행동이다.</item>
    /// <item><b>기물 착지</b> — 놓인 칸마다 짧은 임팩트 VFX/SFX를 흩어 재생한다.</item>
    /// <item><b>흡수</b> — 기물 자리에서 폭발한 뒤, 그 힘이 보스에게 빨려 들어간다.</item>
    /// </list>
    ///
    /// 전부 <b>임시 연출</b>이다: VFX는 카탈로그의 전용 큐(<see cref="BossPropSourceRefs"/> 스탬프)로
    /// 해석되고, SFX는 기존 큐를 빌려 쓴다. 아트가 반입되면 카탈로그 항목과 큐 id만 바꾸면 되고
    /// 이 파일은 그대로다.
    ///
    /// 흡입 연출만 카탈로그가 아니라 코드가 만든다: 파티클을 임의의 월드 좌표로 <b>끌어당기는</b> 것은
    /// 프리팹 저작으로 표현할 수 없고(목적지가 매번 다르다), 아트 반입 시 통째로 갈아 끼울 부분이다.
    /// </summary>
    public sealed partial class MapCombatController
    {
        [Header("Boss Prop (철조각) — 임시 연출")]
        [Tooltip("살포 캐스트에서 경고음이 울린 뒤 보스가 실제로 내려찍기까지의 예열 시간(전조).")]
        [SerializeField] private float bossPropCastTelegraphSeconds = 0.45f;

        [Tooltip("보스 애니메이션이 터진 뒤 기물이 깔리기 시작할 때까지의 텀.")]
        [SerializeField] private float bossPropCastImpactHoldSeconds = 0.35f;

        [Tooltip("기물이 하나씩 꽂히는 간격. 0이면 전부 같은 프레임에 나타난다.")]
        [SerializeField] private float bossPropPlacedStaggerSeconds = 0.08f;

        [SerializeField] private float bossPropCastShakeStrength = 0.45f;
        [SerializeField] private float bossPropCastShakeDuration = 0.4f;

        [Tooltip("흡수 폭발이 터진 뒤 힘이 보스에게 도달하기까지의 비행 시간.")]
        [SerializeField] private float bossPropAbsorbFlightSeconds = 0.35f;

        [Tooltip("여러 기물이 한 번에 흡수될 때 각 폭발 사이의 간격.")]
        [SerializeField] private float bossPropAbsorbStaggerSeconds = 0.06f;

        [Tooltip("보스 살포 캐스트에 쓸 애니메이터 트리거. 보스 애니메이터에 없는 이름이면 조용히 무시된다.")]
        [SerializeField] private string bossPropCastAnimationTrigger = "Attack4";

        [Header("Boss Scrap Chain (철조각 사슬) — 라인 렌더러")]
        [Tooltip("사슬 선 프리팹(LineRenderer 루트). 비어 있으면 코드가 같은 규격의 선을 만든다 — 아트 반입 자리.")]
        [SerializeField] private GameObject bossScrapChainLinePrefab;

        [Tooltip("보스에서 철조각까지 선이 다 그어지는 시간.")]
        [SerializeField] private float bossScrapChainDrawSeconds = 0.35f;

        [Tooltip("여러 가닥이 시작하는 간격.")]
        [SerializeField] private float bossScrapChainStrandStaggerSeconds = 0.05f;

        [Tooltip("선이 다 그어진 뒤 남아 있다가 사라지기까지의 시간.")]
        [SerializeField] private float bossScrapChainHoldSeconds = 0.22f;

        [Tooltip("선이 사라지는(알파 0) 시간.")]
        [SerializeField] private float bossScrapChainFadeSeconds = 0.28f;

        private const float BossScrapChainLineHeight = 0.42f;
        private const float BossScrapChainLineWidth = 0.13f;
        private static readonly Color BossScrapChainLineColor = new Color(1f, 0.85f, 0.25f, 1f);

        private const int BossPropCastShakeVibrato = 9;
        private const float BossPropCastShakeRandomness = 90f;

        // 흡입 파티클(코드 생성)의 규격. 칸 하나가 대략 1 유닛이라는 전제의 placeholder 값이다.
        private const float BossPropMoteRadius = 0.28f;
        private const float BossPropMoteHeight = 0.6f;
        private static readonly Color BossPropMoteColor = new Color(1f, 0.29f, 0.22f, 1f);

        /// <summary>도약 이동(§28 W7): 살포 캐스트와 같은 점프 애니메이션 트리거(Attack4)를 얹고
        /// 착지 지점으로 한 번에 옮긴다 — 걷기 슬라이드로 읽히지 않게 한다.</summary>
        IEnumerator ICombatPresentationSink.LeapEnemyStep(string unitId, HexCoord from, HexCoord to, float seconds)
        {
            if (actorMarkerPresenter != null)
            {
                actorMarkerPresenter.TriggerAttack(unitId, bossPropCastAnimationTrigger ?? string.Empty);
            }

            yield return ((ICombatPresentationSink)this).MoveEnemyStep(unitId, from, to, seconds);
        }

        IEnumerator ICombatPresentationSink.BossPropVolleyCast(string bossUnitId, HexCoord? bossCoord)
        {
            var coord = bossCoord ?? ResolveBossUnitCoord(bossUnitId);
            if (!coord.HasValue || !TryGetTileWorldPosition(coord.Value, out var bossWorld))
            {
                yield break;
            }

            var timing = ResolveTimingProfile();

            // ① 전조: 경고음만 먼저 울린다. 화면에는 아직 아무 변화가 없다 — "무언가 온다"만 남긴다.
            RequestAudioCue(AudioCueIds.MonsterIntentWarning, $"boss-prop-cast:{bossUnitId}");
            // 함정 설치(2026-09-05 결정 2)는 살포와 같은 턴·같은 비트에 일어나고 화면엔 「함정 설치!」 글자뿐이라
            // 소리가 유일한 두 번째 신호다. 전조와 함께 낸다.
            RequestAudioCue(AudioCueIds.BossTrapPlaced, $"boss-trap-placed:{bossUnitId}");
            var telegraph = timing.ScaleDuration(bossPropCastTelegraphSeconds);
            if (telegraph > 0f)
            {
                yield return new WaitForSeconds(telegraph);
            }

            // ② 캐스트: 보스 애니메이션 + 카메라 흔들림 + 캐스트 VFX/SFX가 같은 프레임에 터진다.
            if (actorMarkerPresenter != null)
            {
                actorMarkerPresenter.TriggerAttack(bossUnitId, bossPropCastAnimationTrigger ?? string.Empty);
            }

            PlayBossPropCue(BossPropSourceRefs.VolleyCast, coord.Value, bossWorld);
            RequestAudioCue(AudioCueIds.BossPropVolleyCast, $"boss-prop-cast:{bossUnitId}");
            CameraController.AddCinemachineShake(
                bossPropCastShakeStrength,
                bossPropCastShakeDuration,
                BossPropCastShakeVibrato,
                BossPropCastShakeRandomness);

            // ③ 임팩트가 읽힐 만큼만 붙든다. 이 뒤에 BossPropPlaced 비트들이 이어진다.
            var hold = timing.ScaleDuration(bossPropCastImpactHoldSeconds);
            if (hold > 0f)
            {
                yield return new WaitForSeconds(hold);
            }
        }

        IEnumerator ICombatPresentationSink.BossPropPlaced(string bossUnitId, HexCoord? coord)
        {
            if (!coord.HasValue || !TryGetTileWorldPosition(coord.Value, out var world))
            {
                yield break;
            }

            PlayBossPropCue(BossPropSourceRefs.Placed, coord.Value, world);
            RequestAudioCue(AudioCueIds.BossPropPlaced, $"boss-prop-placed:{coord.Value}");

            var stagger = ResolveTimingProfile().ScaleDuration(bossPropPlacedStaggerSeconds);
            if (stagger > 0f)
            {
                yield return new WaitForSeconds(stagger);
            }
        }

        IEnumerator ICombatPresentationSink.BossPropAbsorb(
            string bossUnitId, HexCoord? propCoord, HexCoord? bossCoord)
        {
            var target = bossCoord ?? ResolveBossUnitCoord(bossUnitId);
            if (!propCoord.HasValue
                || !TryGetTileWorldPosition(propCoord.Value, out var propWorld)
                || !target.HasValue
                || !TryGetTileWorldPosition(target.Value, out var bossWorld))
            {
                yield break;
            }

            // ① 기물 자리에서 폭발. 반경 피해는 규칙이 이미 적용했고 데미지 숫자는 버퍼 이펙트가 낸다 —
            // 이 큐는 폭발 그림만 담당한다.
            PlayBossPropCue(BossPropSourceRefs.AbsorbBlast, propCoord.Value, propWorld);
            RequestAudioCue(AudioCueIds.BossPropAbsorbBlast, $"boss-prop-absorb:{propCoord.Value}");
            // 🔴 폭발과 같은 프레임에 철조각 마커를 끈다(2026-09-05 후속 #8). 흡수는 사망이 아니라 <b>제거</b>라
            //   사망 연출 경로가 없고, 규칙이 RemoveMonster한 기물의 마커는 다음 RefreshView(시퀀스 끝)까지 남아
            //   「터졌는데 그대로 서 있는」 그림이 됐다. 유닛 id는 비트에 없으므로 결의 직후 투영에서 칸으로 찾는다.
            HideAbsorbedBossPropMarker(bossUnitId, propCoord.Value);

            // ② 그 힘이 보스에게 빨려 들어간다. 비행이 끝나기를 기다리지 않고 다음 흡수로 넘어가므로
            // (여러 개가 동시에 빨려 들어가야 "흡수"로 읽힌다) 코루틴을 띄워 두고 짧은 텀만 준다.
            StartCoroutine(FlyBossPropMoteToBoss(propWorld, bossWorld));
            RequestAudioCue(AudioCueIds.BossPropAbsorbPull, $"boss-prop-absorb-pull:{bossUnitId}");

            var stagger = ResolveTimingProfile().ScaleDuration(bossPropAbsorbStaggerSeconds);
            if (stagger > 0f)
            {
                yield return new WaitForSeconds(stagger);
            }
        }

        /// <summary>
        /// 흡수된 힘 한 덩이를 기물 자리에서 보스까지 날린다. 아트 반입 전의 임시 연출이라 프리팹 없이
        /// 코드로 만든다 — 작은 구 + 꼬리. 도착하면 스스로 사라진다.
        ///
        /// 히트스톱(timeScale≈0)과 겹쳐도 흐름이 멈추면 안 되므로 unscaled 시간으로 돈다
        /// (보스 페이즈 전환 펀치줌과 같은 규약).
        /// </summary>
        private IEnumerator FlyBossPropMoteToBoss(Vector3 fromWorld, Vector3 toWorld)
        {
            var mote = CreateBossPropMote(fromWorld);
            if (mote == null)
            {
                yield break;
            }

            var seconds = ResolveTimingProfile().ScaleDuration(bossPropAbsorbFlightSeconds);
            var start = fromWorld + Vector3.up * BossPropMoteHeight;
            var end = toWorld + Vector3.up * BossPropMoteHeight;
            // 살짝 위로 솟았다 빨려 들어가는 아치. 직선이면 "빨려 들어간다"보다 "미끄러진다"로 읽힌다.
            var apex = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 0.8f;

            var elapsed = 0f;
            while (elapsed < seconds && mote != null)
            {
                var t = Mathf.Clamp01(elapsed / seconds);
                // 뒤로 갈수록 빨라지는 이징 = 빨려 들어가는 가속감.
                var eased = t * t;
                var a = Vector3.Lerp(start, apex, eased);
                var b = Vector3.Lerp(apex, end, eased);
                mote.transform.position = Vector3.Lerp(a, b, eased);
                mote.transform.localScale = Vector3.one * Mathf.Lerp(BossPropMoteRadius * 2f, 0.05f, eased);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (mote != null)
            {
                Destroy(mote);
            }
        }

        private GameObject CreateBossPropMote(Vector3 worldPosition)
        {
            var mote = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mote.name = "BossPropAbsorbMote";
            // 충돌체는 클릭 히트테스트(몬스터 툴팁·카드 타깃)를 가로챈다. 연출 오브젝트는 절대 남기지 않는다.
            var collider = mote.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            mote.transform.SetParent(transform, worldPositionStays: false);
            mote.transform.position = worldPosition + Vector3.up * BossPropMoteHeight;
            mote.transform.localScale = Vector3.one * (BossPropMoteRadius * 2f);

            var renderer = mote.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = BossPropMoteColor;
                    // URP Unlit은 _BaseColor를 본다(_Color만 세우면 흰색으로 나온다).
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", BossPropMoteColor);
                    }

                    renderer.sharedMaterial = material;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return mote;
        }

        /// <summary>
        /// 기물 연출 큐 하나를 재생한다. 보스 페이즈 전환 버스트와 같은 규약이다 — 전용
        /// <c>sourceRef</c> 스탬프로 카탈로그 항목을 고르고, amount 0 + "field" 타깃이라
        /// 플로팅 텍스트를 내지 않는다.
        /// </summary>
        private void PlayBossPropCue(string sourceRef, HexCoord coord, Vector3 worldPosition)
        {
            var presentation = ResolveMovementEffectPresentation();
            if (presentation == null)
            {
                return;
            }

            presentation.Play(
                new EffectResultEvent(
                    EffectKind.StatusEffectApplied,
                    targetUnitId: "field",
                    center: coord,
                    sourceRef: sourceRef),
                worldPosition,
                Quaternion.identity);
        }

        /// <summary>흡수된 철조각의 마커를 즉시 숨긴다. 다음 뷰 갱신이 죽은 유닛의 마커를 정리한다(파괴는 그쪽 몫).</summary>
        private void HideAbsorbedBossPropMarker(string bossUnitId, HexCoord propCoord)
        {
            if (State == null || actorMarkerPresenter == null)
            {
                return;
            }

            foreach (var absorption in State.LastBossPropAbsorptions)
            {
                if (absorption.PropCoord.Equals(propCoord)
                    && string.Equals(absorption.BossUnitId, bossUnitId, System.StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(absorption.PropUnitId))
                {
                    actorMarkerPresenter.SetActive(absorption.PropUnitId, false);
                    return;
                }
            }
        }

        /// <summary>
        /// 사슬 명중 비트(2026-09-05 후속 #7): 가닥마다 보스→철조각 셀 중심을 따라 선이 <b>점진적으로</b> 그어지고,
        /// 다 그어진 순간 플레이어가 맞은 칸이면 피격 큐·전격 SFX·짧은 흔들림이 난다. 선은 잠깐 남았다 사라진다.
        /// 여러 가닥은 짧은 텀으로 함께 시작한다(하나씩 순서대로면 「사슬」이 아니라 「레이저 순서」로 읽힌다).
        /// </summary>
        IEnumerator ICombatPresentationSink.BossScrapChainHit(string bossUnitId, HexCoord? bossCoord, HexCoord? hitPlayerCoord)
        {
            if (State == null)
            {
                yield break;
            }

            IReadOnlyList<IReadOnlyList<HexCoord>> strands = null;
            foreach (var hit in State.LastBossScrapChainHits)
            {
                if (string.Equals(hit.BossUnitId, bossUnitId, System.StringComparison.Ordinal))
                {
                    strands = hit.Strands;
                    break;
                }
            }

            if (strands == null || strands.Count == 0)
            {
                yield break;
            }

            var timing = ResolveTimingProfile();
            var draw = timing.ScaleDuration(bossScrapChainDrawSeconds);
            var stagger = timing.ScaleDuration(bossScrapChainStrandStaggerSeconds);
            var lines = new List<LineRenderer>();
            var strandHitsPlayer = false;
            foreach (var strand in strands)
            {
                var points = new List<Vector3>(strand.Count + 1);
                if (bossCoord.HasValue && TryGetTileWorldPosition(bossCoord.Value, out var bossWorld))
                {
                    points.Add(bossWorld + Vector3.up * BossScrapChainLineHeight);
                }

                foreach (var cell in strand)
                {
                    if (TryGetTileWorldPosition(cell, out var world))
                    {
                        points.Add(world + Vector3.up * BossScrapChainLineHeight);
                    }
                }

                if (points.Count < 2)
                {
                    continue;
                }

                if (hitPlayerCoord.HasValue && ContainsCoord(strand, hitPlayerCoord.Value))
                {
                    strandHitsPlayer = true;
                }

                var line = CreateBossScrapChainLine();
                if (line == null)
                {
                    continue;
                }

                lines.Add(line);
                StartCoroutine(DrawBossScrapChainLine(line, points, draw));
                if (stagger > 0f)
                {
                    yield return new WaitForSecondsRealtime(stagger);
                }
            }

            if (lines.Count == 0)
            {
                yield break;
            }

            // 마지막 가닥이 다 그어질 때까지 — 선이 닿는 순간이 명중이다.
            if (draw > 0f)
            {
                yield return new WaitForSecondsRealtime(draw);
            }

            if (hitPlayerCoord.HasValue && strandHitsPlayer && TryGetTileWorldPosition(hitPlayerCoord.Value, out var playerWorld))
            {
                PlayBossPropCue(CombatState.BossScrapChainSourceRefs.Hit, hitPlayerCoord.Value, playerWorld);
                RequestAudioCue(AudioCueIds.BossScrapChainHit, $"boss-scrap-chain:{bossUnitId}");
                CameraController.AddCinemachineShake(
                    bossPropCastShakeStrength * 0.6f,
                    bossPropCastShakeDuration * 0.6f,
                    BossPropCastShakeVibrato,
                    BossPropCastShakeRandomness);
            }

            var hold = timing.ScaleDuration(bossScrapChainHoldSeconds);
            if (hold > 0f)
            {
                yield return new WaitForSecondsRealtime(hold);
            }

            // 사라지는 것은 기다리지 않는다 — 다음 비트(피해 숫자)가 선 위에 겹쳐 읽힌다.
            StartCoroutine(FadeAndDestroyBossScrapChainLines(lines, timing.ScaleDuration(bossScrapChainFadeSeconds)));
        }

        private static bool ContainsCoord(IReadOnlyList<HexCoord> cells, HexCoord coord)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                if (cells[i].Equals(coord))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>선 하나를 점진적으로 긋는다: 끝점이 셀 중심을 차례로 지나며 자란다. unscaled 시간(히트스톱과 겹친다).</summary>
        private static IEnumerator DrawBossScrapChainLine(LineRenderer line, IReadOnlyList<Vector3> points, float seconds)
        {
            if (line == null)
            {
                yield break;
            }

            var segments = points.Count - 1;
            var elapsed = 0f;
            while (seconds > 0f && elapsed < seconds && line != null)
            {
                var t = Mathf.Clamp01(elapsed / seconds);
                var scaled = t * segments;
                var full = Mathf.FloorToInt(scaled);
                var frac = scaled - full;
                var count = Mathf.Min(full + 2, points.Count);
                line.positionCount = count;
                for (var i = 0; i < count - 1; i++)
                {
                    line.SetPosition(i, points[i]);
                }

                var tail = full + 1 < points.Count
                    ? Vector3.Lerp(points[full], points[full + 1], frac)
                    : points[points.Count - 1];
                line.SetPosition(count - 1, tail);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (line != null)
            {
                line.positionCount = points.Count;
                for (var i = 0; i < points.Count; i++)
                {
                    line.SetPosition(i, points[i]);
                }
            }
        }

        private static IEnumerator FadeAndDestroyBossScrapChainLines(List<LineRenderer> lines, float seconds)
        {
            var elapsed = 0f;
            while (seconds > 0f && elapsed < seconds)
            {
                var alpha = 1f - Mathf.Clamp01(elapsed / seconds);
                foreach (var line in lines)
                {
                    if (line == null)
                    {
                        continue;
                    }

                    var color = BossScrapChainLineColor;
                    color.a = alpha;
                    line.startColor = color;
                    line.endColor = color;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            foreach (var line in lines)
            {
                if (line != null)
                {
                    Destroy(line.gameObject);
                }
            }
        }

        /// <summary>
        /// 사슬 선 한 가닥. 프리팹(<see cref="bossScrapChainLinePrefab"/>)이 있으면 그것, 없으면 코드가 같은 규격
        /// (폭 0.13 · 전격 노랑 · URP Unlit)으로 만든다 — 발주(V-7 라인 머티리얼)가 오면 프리팹 슬롯만 채우면 된다.
        /// </summary>
        private LineRenderer CreateBossScrapChainLine()
        {
            GameObject root;
            if (bossScrapChainLinePrefab != null)
            {
                root = Instantiate(bossScrapChainLinePrefab, transform, false);
            }
            else
            {
                root = new GameObject("BossScrapChainLine");
                root.transform.SetParent(transform, false);
                var created = root.AddComponent<LineRenderer>();
                created.useWorldSpace = true;
                created.alignment = LineAlignment.View;
                created.textureMode = LineTextureMode.Stretch;
                created.numCornerVertices = 2;
                created.numCapVertices = 2;
                created.startWidth = BossScrapChainLineWidth;
                created.endWidth = BossScrapChainLineWidth * 0.7f;
                created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.receiveShadows = false;
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var material = new Material(shader);
                    material.color = BossScrapChainLineColor;
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", BossScrapChainLineColor);
                    }

                    created.sharedMaterial = material;
                }
            }

            var line = root.GetComponent<LineRenderer>() ?? root.GetComponentInChildren<LineRenderer>();
            if (line == null)
            {
                Destroy(root);
                return null;
            }

            line.positionCount = 0;
            line.startColor = BossScrapChainLineColor;
            line.endColor = BossScrapChainLineColor;
            return line;
        }

        /// <summary>보스의 공격 SFX 큐. 정의 id를 못 찾으면 카탈로그의 공용 경고음으로 떨어진다.</summary>
        private string ResolveBossAttackCueId(string bossUnitId)
        {
            if (State != null && !string.IsNullOrEmpty(bossUnitId))
            {
                foreach (var monster in State.Monsters)
                {
                    if (string.Equals(monster.Id, bossUnitId, System.StringComparison.Ordinal))
                    {
                        return AudioCueIds.MonsterAttack(monster.DefinitionId);
                    }
                }
            }

            return AudioCueIds.MonsterIntentWarning;
        }
    }
}
