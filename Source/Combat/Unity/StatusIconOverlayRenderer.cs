using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Data-driven icon renderer for the combat overlay presentation's annotation channel.
    /// Spawns one billboarded status-icon GameObject per <see cref="CombatOverlayIconAnnotation"/>
    /// and cycles through multiple effects on a tile. This is the pure view logic carved out of the
    /// legacy MonsterAttackStatusIconOverlay: it no longer queries combat state, computes intent, or
    /// decides visibility. The owning presenter feeds it annotations; world placement comes from the
    /// injected <see cref="IHexMapOverlaySurfaceProjector"/> (no bespoke hex-to-world math). Icons
    /// live under this component's own transform so the tile presentation view's ClearAll/Render
    /// sweeps and click raycasts never touch them.
    /// </summary>
    public sealed class StatusIconOverlayRenderer : MonoBehaviour, ICombatOverlayIconRenderer
    {
        [Header("Layout")]
        [SerializeField] private float iconHeight = 0.03f;
        // 실효값은 씬 직렬화가 정본이다 — 이 초기값만 고치면 이미 저장된 씬은 옛 값을 유지한다.
        // 가시성 판정(2026-08-07 실플레이)으로 0.7 → 1.0. 호버 콜라이더도 이 값을 쓰므로 판정 면적이 함께 는다.
        [SerializeField] private float iconSize = 1f;
        [SerializeField] private Vector3 iconEulerAngles = new Vector3(90f, 0f, 0f);
        [SerializeField] private bool faceCamera;
        [SerializeField, Range(0f, 1f)] private float iconOpacity = 0.5f;
        [SerializeField] private float cycleInterval = 1f;
        [SerializeField] private float hoverColliderThickness = 0.12f;
        [SerializeField] private float hoverScreenRadiusPixels = 30f;
        [SerializeField] private float hoverProxyRadius = 0.42f;

        [Header("Icon Background")]
        [SerializeField] private bool removeConnectedWhiteBackground = true;
        [SerializeField, Range(0.5f, 1f)] private float whiteBackgroundCutoff = 0.88f;
        // §28 W3: 출하 아이콘 17종은 다크 네이비 판(가장자리 RGB ≤ ~49)이 통째로 구워져 있다.
        // HUD·툴팁은 판을 그대로 쓰고, 타일 오버레이(=이 렌더러)만 판을 걷어낸다 — 흰 배경 제거와
        // 같은 가장자리 플러드필이라 글리프 안쪽의 어두운 선은 안전하다. 0.3은 판 그라디언트를 다
        // 덮으면서 글리프 글로우는 남기는 값(시뮬레이션 검증: 제거율 67~87%, 글리프 무손상).
        [SerializeField, Range(0f, 0.5f)] private float darkBackgroundCutoff = 0.3f;

        [Header("Status Effect Icon Catalog")]
        [SerializeField] private StatusEffectIconCatalog iconCatalog;

        // Shared so other HUD presenters (e.g. the monster hover tooltip) can render the same
        // status-effect iconography the map overlay uses without wiring a second catalog reference.
        public StatusEffectIconCatalog IconCatalog => iconCatalog;

        [Header("Status Effect Sprites (indexed by StatusEffectKind)")]
        [Tooltip("7 slots in StatusEffectKind enum order: 0=Immobilize, 1=Poison, 2=Stun, 3=Slow, 4=Rupture, 5=Reflect, 6=Agility")]
        [SerializeField] private Sprite[] effectKindSprites = new Sprite[7];

        private IHexMapOverlaySurfaceProjector projector;
        private Transform mapSpaceRoot;
        private Camera hoverCamera;

        // 호버 중 순환 정지(#15): 마지막으로 호버가 확인된 그룹과 그 프레임 스탬프. 폴링은 매 프레임
        // 이므로 스탬프가 1프레임 이상 낡으면 호버가 풀린 것으로 본다(시네마틱·일시정지로 폴링이
        // 끊겨도 sticky하게 얼어붙지 않는다).
        private int hoveredGroupIndex = -1;
        private int hoveredFrame = -1;

        private readonly List<TileIconGroup> groups = new List<TileIconGroup>();
        private readonly Dictionary<Sprite, Sprite> whiteKeyedSpriteCache = new Dictionary<Sprite, Sprite>();
        private Sprite fallbackSprite;
        private Material iconMaterial;
        private float cycleTimer;

        public readonly struct TooltipPayload
        {
            private TooltipPayload(
                bool isKnockback,
                ActiveEffect effect,
                int annihilationTurns,
                int annihilationDamage,
                bool isSafeZoneCandidate = false,
                int weakSpotDamagePercent = 0,
                int weakSpotTurnsRemaining = 0,
                bool isSafeZoneConfirmed = false,
                int knockbackDistance = 0,
                bool isCurse = false)
            {
                IsCurse = isCurse;
                IsSafeZoneCandidate = isSafeZoneCandidate;
                WeakSpotDamagePercent = weakSpotDamagePercent;
                WeakSpotTurnsRemaining = weakSpotTurnsRemaining;
                IsKnockback = isKnockback;
                KnockbackDistance = knockbackDistance;
                Effect = effect;
                AnnihilationTurnsRemaining = annihilationTurns;
                AnnihilationDamage = annihilationDamage;
                IsSafeZoneConfirmed = isSafeZoneConfirmed;
            }

            public bool IsKnockback { get; }

            /// <summary>변위 칸 수 — 양수=밀치기 · 음수=끌어당김(§16.1). 툴팁 제목이 이 부호로 갈린다.</summary>
            public int KnockbackDistance { get; }

            /// <summary>호버한 변위 표식이 끌어당김인가.</summary>
            public bool IsPull => IsKnockback && KnockbackDistance < 0;

            public ActiveEffect Effect { get; }

            /// <summary>전멸기 예고까지 남은 몬스터 행동 수(0 = 전멸기 툴팁이 아니다).</summary>
            public int AnnihilationTurnsRemaining { get; }
            public int AnnihilationDamage { get; }
            public bool IsAnnihilation => AnnihilationTurnsRemaining > 0;

            public static TooltipPayload Status(ActiveEffect effect) => new TooltipPayload(false, effect, 0, 0);
            public static TooltipPayload Knockback(int distance) =>
                new TooltipPayload(true, default, 0, 0, knockbackDistance: distance);
            public static TooltipPayload Annihilation(int turnsRemaining, int damage) =>
                new TooltipPayload(false, default, turnsRemaining, damage);

            public bool IsSafeZoneCandidate { get; }
            public int WeakSpotDamagePercent { get; }
            public int WeakSpotTurnsRemaining { get; }
            public bool IsWeakSpot => WeakSpotDamagePercent > 0;

            public static TooltipPayload SafeZoneCandidate() =>
                new TooltipPayload(false, default, 0, 0, isSafeZoneCandidate: true);

            public static TooltipPayload WeakSpot(int damagePercent, int turnsRemaining) =>
                new TooltipPayload(false, default, 0, 0, weakSpotDamagePercent: damagePercent, weakSpotTurnsRemaining: turnsRemaining);

            /// <summary>정찰로 판명된 진짜 안전지대(§28 W5 후속 T1) — 초록 확정 채움의 호버 툴팁.</summary>
            public bool IsSafeZoneConfirmed { get; }

            public static TooltipPayload SafeZoneConfirmed() =>
                new TooltipPayload(false, default, 0, 0, isSafeZoneConfirmed: true);

            /// <summary>「저주 부여」 표식을 호버했는가(2026-09-01 W2).</summary>
            public bool IsCurse { get; }

            public static TooltipPayload Curse() =>
                new TooltipPayload(false, default, 0, 0, isCurse: true);
        }

        /// <summary>
        /// A single icon in a tile's telegraph cycle. Either a status effect (keyed by
        /// <see cref="StatusEffectKind"/>) or a knockback marker, which is an instant displacement
        /// rather than a persistent status and so is not part of the status enum.
        /// </summary>
        private readonly struct OverlayIcon
        {
            public readonly bool IsKnockback;

            /// <summary>변위 칸 수 — 양수=밀치기 · 음수=끌어당김(§16.1). <see cref="IsKnockback"/>일 때만 뜻이 있다.</summary>
            public readonly int KnockbackDistance;

            public readonly ActiveEffect Effect;

            /// <summary>0보다 크면 전멸기 경고 아이콘이다(§13.5) — 상태이상도 넉백도 아니다.</summary>
            public readonly int AnnihilationTurnsRemaining;
            public readonly int AnnihilationDamage;

            /// <summary>미판별 안전지대 후보 = <c>?</c>(§20-B-6). 이 축의 유일한 신규 기호다.</summary>
            public readonly bool IsSafeZoneCandidate;

            /// <summary>취약 부위 피해 증가율(%·0 = 아님 · §28 W4).</summary>
            public readonly int WeakSpotDamagePercent;
            public readonly int WeakSpotTurnsRemaining;

            /// <summary>정찰로 판명된 진짜 안전지대(§28 W5 후속 T1). <b>그리지 않는 호버 전용 표식</b> —
            /// 초록 확정 채움이 이미 시각 신호라, 이 아이콘의 일은 툴팁 호버 대상을 만드는 것뿐이다.</summary>
            public readonly bool IsSafeZoneConfirmed;

            /// <summary>「저주 부여」 표식(2026-09-01 W2) — 상태이상도 넉백도 아니다. 이 칸에 서면
            /// 덱에 저주 카드가 섞인다는 예고이고, 어느 카드인지는 명중 시점에 뽑히므로 말하지 않는다.</summary>
            public readonly bool IsCurse;

            private OverlayIcon(
                bool isKnockback,
                ActiveEffect effect,
                int annihilationTurns,
                int annihilationDamage,
                bool isSafeZoneCandidate = false,
                int weakSpotDamagePercent = 0,
                int weakSpotTurnsRemaining = 0,
                bool isSafeZoneConfirmed = false,
                int knockbackDistance = 0,
                bool isCurse = false)
            {
                IsCurse = isCurse;
                IsKnockback = isKnockback;
                KnockbackDistance = knockbackDistance;
                Effect = effect;
                AnnihilationTurnsRemaining = annihilationTurns;
                AnnihilationDamage = annihilationDamage;
                IsSafeZoneCandidate = isSafeZoneCandidate;
                WeakSpotDamagePercent = weakSpotDamagePercent;
                WeakSpotTurnsRemaining = weakSpotTurnsRemaining;
                IsSafeZoneConfirmed = isSafeZoneConfirmed;
            }

            public StatusEffectKind Kind => Effect.Kind;
            public bool IsAnnihilation => AnnihilationTurnsRemaining > 0;

            public bool IsPull => IsKnockback && KnockbackDistance < 0;

            public static OverlayIcon Status(ActiveEffect effect) => new OverlayIcon(false, effect, 0, 0);
            public static OverlayIcon Knockback(int distance) =>
                new OverlayIcon(true, default, 0, 0, knockbackDistance: distance);
            public static OverlayIcon Annihilation(int turnsRemaining, int damage) =>
                new OverlayIcon(false, default, turnsRemaining, damage);
            public static OverlayIcon SafeZoneCandidate() =>
                new OverlayIcon(false, default, 0, 0, isSafeZoneCandidate: true);
            public bool IsWeakSpot => WeakSpotDamagePercent > 0;
            public static OverlayIcon WeakSpot(int damagePercent, int turnsRemaining) =>
                new OverlayIcon(false, default, 0, 0, weakSpotDamagePercent: damagePercent, weakSpotTurnsRemaining: turnsRemaining);
            public static OverlayIcon SafeZoneConfirmed() =>
                new OverlayIcon(false, default, 0, 0, isSafeZoneConfirmed: true);
            public static OverlayIcon Curse() =>
                new OverlayIcon(false, default, 0, 0, isCurse: true);
        }

        private sealed class TileIconGroup
        {
            public HexCoord Coord;
            public List<OverlayIcon> Icons;
            public GameObject Root;
            public SpriteRenderer SpriteRend;
            public TextMeshPro Label;
            public Collider HoverCollider;
            public int CurrentIndex;

            /// <summary>피해 배지만 있고 순환할 아이콘이 없는 칸이 있다(순수 피해 공격).</summary>
            public bool HasIcons => Icons != null && Icons.Count > 0;

            public OverlayIcon CurrentIcon => Icons[CurrentIndex];
        }

        /// <summary>
        /// Wires the world-placement seam. <paramref name="mapSpaceRoot"/> is the transform that
        /// <see cref="IHexMapOverlaySurfaceProjector.ProjectOverlaySurface"/> returns positions
        /// relative to (the tile presentation root); when null the projector output is treated as a
        /// world position directly (used by tests with a fake projector).
        /// </summary>
        public void Configure(IHexMapOverlaySurfaceProjector projector, Transform mapSpaceRoot = null)
        {
            this.projector = projector;
            this.mapSpaceRoot = mapSpaceRoot;
        }

        public void SetHoverCamera(Camera camera)
        {
            hoverCamera = camera;
            // 아이콘 정렬(#15)도 호버 판정과 같은 카메라를 본다 — 둘이 갈라지면 "보이는 곳"과
            // "잡히는 곳"이 어긋난다.
            foreach (var group in groups)
            {
                if (group.Root != null && group.Root.TryGetComponent<StatusIconCameraFacing>(out var facing))
                {
                    facing.SetCamera(camera);
                }
            }
        }

        public bool TryGetHoveredStatus(Camera camera, Vector2 screenPos, out ActiveEffect effect)
        {
            if (TryGetHoveredTooltip(camera, screenPos, out var payload) && !payload.IsKnockback && !payload.IsAnnihilation)
            {
                effect = payload.Effect;
                return true;
            }

            effect = default;
            return false;
        }

        public bool TryGetHoveredTooltip(Camera camera, Vector2 screenPos, out TooltipPayload payload)
        {
            payload = default;
            var hitCamera = camera != null ? camera : hoverCamera != null ? hoverCamera : Camera.main;
            if (hitCamera == null)
            {
                return false;
            }

            if (!TryGetRaycastGroup(hitCamera, screenPos, out var group) &&
                !TryGetNearestScreenGroup(hitCamera, screenPos, out group))
            {
                return false;
            }

            // 피해 배지만 있는 칸(순수 피해 공격)은 순환할 아이콘이 없다 — 호버로 답할 내용도 없다.
            if (!group.HasIcons)
            {
                return false;
            }

            // 호버 중 순환 정지(2026-08-20 #15): 설명을 읽는 동안 아이콘·툴팁이 넘어가면 안 된다.
            // 프레임 스탬프 방식인 이유 — 호버 폴링은 시네마틱·일시정지 중 건너뛰므로, sticky bool로
            // 잡으면 그 사이 호버였던 칸이 영원히 얼어붙는다.
            var groupIndex = groups.IndexOf(group);
            if (groupIndex >= 0)
            {
                if (hoveredGroupIndex != groupIndex)
                {
                    // 호버 진입: 타이머를 되감아 손을 뗀 뒤에도 한 인터벌을 온전히 보장한다 —
                    // 아니면 뗀 직후 곧바로 넘어가 "정지"가 없던 일처럼 보인다.
                    cycleTimer = 0f;
                }

                hoveredGroupIndex = groupIndex;
                hoveredFrame = Time.frameCount;
            }

            var icon = group.CurrentIcon;
            if (icon.IsWeakSpot)
            {
                payload = TooltipPayload.WeakSpot(icon.WeakSpotDamagePercent, icon.WeakSpotTurnsRemaining);
                return true;
            }

            if (icon.IsSafeZoneConfirmed)
            {
                payload = TooltipPayload.SafeZoneConfirmed();
                return true;
            }

            if (icon.IsSafeZoneCandidate)
            {
                payload = TooltipPayload.SafeZoneCandidate();
                return true;
            }

            if (icon.IsAnnihilation)
            {
                payload = TooltipPayload.Annihilation(icon.AnnihilationTurnsRemaining, icon.AnnihilationDamage);
                return true;
            }

            if (icon.IsKnockback)
            {
                payload = TooltipPayload.Knockback(icon.KnockbackDistance);
                return true;
            }

            if (icon.IsCurse)
            {
                payload = TooltipPayload.Curse();
                return true;
            }

            payload = TooltipPayload.Status(icon.Effect);
            return true;
        }

        public void ApplyAnnotations(IReadOnlyList<CombatOverlayIconAnnotation> annotations)
        {
            Rebuild(annotations);
        }

        public void Clear()
        {
            ClearAll();
        }

        private void Update()
        {
            if (cycleInterval <= 0f || !HasCyclingGroups())
            {
                return;
            }

            cycleTimer += Time.deltaTime;
            if (cycleTimer >= cycleInterval)
            {
                cycleTimer = 0f;
                AdvanceCycle();
            }
        }

        private void OnDestroy()
        {
            // Icon GameObjects are children of this transform and are destroyed with it; only the
            // generated material/sprite/texture instances need explicit cleanup here.
            if (iconMaterial != null)
            {
                Destroy(iconMaterial);
                iconMaterial = null;
            }

            if (fallbackSprite != null)
            {
                if (fallbackSprite.texture != null)
                {
                    Destroy(fallbackSprite.texture);
                }
                Destroy(fallbackSprite);
                fallbackSprite = null;
            }

            foreach (var sprite in whiteKeyedSpriteCache.Values)
            {
                if (sprite == null)
                {
                    continue;
                }
                if (sprite.texture != null)
                {
                    Destroy(sprite.texture);
                }
                Destroy(sprite);
            }
            whiteKeyedSpriteCache.Clear();
        }

        private void Rebuild(IReadOnlyList<CombatOverlayIconAnnotation> annotations)
        {
            // 살아남는 좌표의 순환 위치를 보존한다(#15): 오버레이 갱신은 그룹을 전부 다시 만드는데,
            // 그때마다 0번으로 되감으면 호버로 읽던 아이콘이 갱신 한 번에 다른 그림으로 바뀐다.
            var previousIndexByCoord = new Dictionary<HexCoord, int>();
            foreach (var group in groups)
            {
                previousIndexByCoord[group.Coord] = group.CurrentIndex;
            }

            ClearAll();
            if (annotations == null || annotations.Count == 0)
            {
                return;
            }

            foreach (var annotation in annotations)
            {
                var icons = BuildIcons(annotation);
                if (icons.Count == 0)
                {
                    continue;
                }

                var group = SpawnGroup(annotation.Coord, icons, groups.Count);
                if (previousIndexByCoord.TryGetValue(group.Coord, out var previousIndex) &&
                    previousIndex > 0 && previousIndex < group.Icons.Count)
                {
                    group.CurrentIndex = previousIndex;
                }

                groups.Add(group);
                if (group.HasIcons)
                {
                    UpdateVisual(group, group.Icons[group.CurrentIndex]);
                }
            }

            cycleTimer = 0f;
        }

        private static List<OverlayIcon> BuildIcons(CombatOverlayIconAnnotation annotation)
        {
            var icons = new List<OverlayIcon>();
            if (annotation.StatusEffects != null && annotation.StatusEffects.Count > 0)
            {
                foreach (var effect in annotation.StatusEffects)
                {
                    icons.Add(OverlayIcon.Status(effect));
                }
            }
            else if (annotation.Effects != null)
            {
                foreach (var kind in annotation.Effects)
                {
                    icons.Add(OverlayIcon.Status(new ActiveEffect(EffectType.Duration, kind, string.Empty, 0)));
                }
            }

            if (annotation.Knockback)
            {
                icons.Add(OverlayIcon.Knockback(annotation.KnockbackDistance));
            }

            // 저주는 순환의 <b>맨 뒤</b>다. 전멸기 경고·안전지대 후보와 달리 "지금 이동을 결정하라"가
            // 아니라 "맞으면 덱이 더러워진다"는 정보라, 이 칸의 즉발 위협(상태이상·변위)보다 뒤에 읽혀도 된다.
            if (annotation.InjectsCurse)
            {
                icons.Add(OverlayIcon.Curse());
            }

            // 전멸기 경고는 사이클 맨 앞에 세운다 — 이 칸의 다른 어떤 예고보다 먼저 읽혀야 한다
            // (다른 예고는 피해를 깎지만 이건 이동을 강제한다).
            if (annotation.HasAnnihilationWarning)
            {
                icons.Insert(0, OverlayIcon.Annihilation(
                    annotation.AnnihilationTurnsRemaining, annotation.AnnihilationDamage));
            }

            // `?`(미판별 안전지대 후보)도 사이클 맨 앞이다 — 이 칸의 다른 어떤 예고보다 먼저 읽혀야
            // 하는 이유가 전멸기 경고와 같다: "여기로 갈지 말지"를 지금 결정해야 한다.
            if (annotation.SafeZoneCandidate)
            {
                icons.Insert(0, OverlayIcon.SafeZoneCandidate());
            }

            // 취약 부위(§28 W4)도 사이클 맨 앞 — "이 칸을 치면 더 아프다"는 이번 공격 결정에 걸린 정보다.
            if (annotation.HasWeakSpotMark)
            {
                icons.Insert(0, OverlayIcon.WeakSpot(annotation.WeakSpotDamagePercent, annotation.WeakSpotTurnsRemaining));
            }

            // 확정 안전지대(§28 W5 후속 T1)는 <b>다른 아이콘이 없을 때만</b> 넣는다. 이 표식은 아무것도
            // 그리지 않는 호버 전용이라, 그리는 아이콘과 한 사이클에 섞이면 순환마다 아이콘이 통째로
            // 깜빡여 보인다(빈 슬롯이 차례가 되는 1초). 겹치는 경우(확정 칸에 상태이상 예고가 얹힘)는
            // 그리는 표식의 툴팁이 우선한다 — 위험 정보가 안전 확인보다 급하다.
            if (annotation.SafeZoneConfirmed && icons.Count == 0)
            {
                icons.Add(OverlayIcon.SafeZoneConfirmed());
            }

            return icons;
        }

        private bool HasCyclingGroups()
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Icons.Count > 1)
                {
                    return true;
                }
            }
            return false;
        }

        private void AdvanceCycle()
        {
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Icons.Count <= 1 || IsGroupHoverFrozen(i))
                {
                    continue;
                }
                group.CurrentIndex = (group.CurrentIndex + 1) % group.Icons.Count;
                UpdateVisual(group, group.Icons[group.CurrentIndex]);
            }
        }

        /// <summary>
        /// 이 그룹이 호버로 얼어 있는가(#15). 폴링은 매 프레임이므로 스탬프가 1프레임 이상 낡으면
        /// 호버가 풀린 것이다 — 얼림은 호버된 그 칸에만 걸리고 다른 칸의 순환은 계속 돈다.
        /// </summary>
        private bool IsGroupHoverFrozen(int groupIndex)
        {
            return groupIndex == hoveredGroupIndex &&
                   hoveredFrame >= 0 &&
                   Time.frameCount - hoveredFrame <= 1;
        }

        private TileIconGroup SpawnGroup(HexCoord coord, List<OverlayIcon> icons, int groupIndex)
        {
            var root = new GameObject($"StatusIcon [{coord.Q},{coord.R}]");
            root.transform.SetParent(transform, false);
            root.transform.position = ResolveWorldPosition(coord);
            root.transform.localScale = Vector3.one;
            // 회전은 StatusIconCameraFacing이 소유한다(#15): 눕힌 데칼 + 카메라 yaw 추종. 여기서 월드
            // 고정 각을 박으면 카메라를 돌릴 때 화면상 회전·반전으로 보인다(옛 버그).
            var cameraFacing = root.AddComponent<StatusIconCameraFacing>();
            cameraFacing.SetCamera(hoverCamera);
            cameraFacing.Configure(faceCamera, iconEulerAngles);

            var sr = root.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 100;
            sr.sharedMaterial = GetIconMaterial();

            var hoverCollider = root.AddComponent<BoxCollider>();
            hoverCollider.isTrigger = true;
            hoverCollider.size = new Vector3(iconSize, iconSize, Mathf.Max(0.01f, hoverColliderThickness));

            var group = new TileIconGroup
            {
                Coord = coord,
                Icons = icons,
                Root = root,
                SpriteRend = sr,
                Label = null,
                HoverCollider = hoverCollider,
                CurrentIndex = 0
            };
            var hoverTarget = root.AddComponent<StatusIconHoverTarget>();
            hoverTarget.Owner = this;
            hoverTarget.GroupIndex = groupIndex;

            CreateInvisibleHoverProxy(root.transform, groupIndex);
            return group;
        }

        private void CreateInvisibleHoverProxy(Transform parent, int groupIndex)
        {
            var proxy = new GameObject("StatusIcon Hover Proxy");
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one;

            // This is intentionally a real 3D hover target even though it renders nothing. The icon
            // itself is a flat sprite laid onto the tile surface, where collider hits can be lost to
            // nearby tile/prop geometry. A small spherical proxy gives the same world-object hover
            // behavior as monster/field-object tooltips without visually covering the tile.
            var collider = proxy.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = Mathf.Max(0.05f, hoverProxyRadius);

            var hoverTarget = proxy.AddComponent<StatusIconHoverTarget>();
            hoverTarget.Owner = this;
            hoverTarget.GroupIndex = groupIndex;
        }

        private void UpdateVisual(TileIconGroup group, OverlayIcon icon)
        {
            // 확정 안전지대(§28 W5 후속 T1)는 아무것도 그리지 않는다 — 초록 확정 채움이 이미 시각
            // 신호고, 이 그룹의 존재 이유는 호버 콜라이더뿐이다. 폴백 경로(흰 칩 + 라벨)로 떨어지면
            // 안 되므로 여기서 명시적으로 비운다. 글리프가 필요해지면(사용자 판정) TryResolveGlyphOnlyIcon에
            // 분기 하나를 추가하면 된다.
            if (icon.IsSafeZoneConfirmed)
            {
                group.SpriteRend.sprite = null;
                if (group.Label != null)
                {
                    group.Label.text = string.Empty;
                    group.Label.gameObject.SetActive(false);
                }
                return;
            }

            var sprite = GetIconSprite(icon);
            if (sprite != null)
            {
                var displaySprite = GetDisplaySprite(sprite);
                group.SpriteRend.sprite = displaySprite;
                group.SpriteRend.color = WithOpacity(Color.white);
                if (group.Label != null)
                {
                    group.Label.text = string.Empty;
                    group.Label.gameObject.SetActive(false);
                }
                ApplySpriteScale(group.SpriteRend, displaySprite);
            }
            else if (TryResolveGlyphOnlyIcon(icon, out var glyphText, out var glyphColor, out var glyphSize))
            {
                // 🔴 <b>받침(판)을 그리지 않는다</b> — 글리프만 크게 뜬다(사용자 확정).
                //
                // 폴백의 "판"은 디자인된 요소가 아니라 <see cref="GetFallbackSprite"/>가 만드는 4×4 흰
                // 텍스처다. 아트가 없을 때 색만 칠해 뭐라도 띄우는 임시 받침이며, 그 판이 자기가 앉는
                // 오버레이와 같은 색이 되는 순간 통째로 사라진다(실기 캡처가 잡은 결함). 게다가 글자는
                // 판 안에 갇혀 작아진다 — 판을 지우면 두 문제가 함께 사라진다.
                // 대비는 판이 아니라 <b>글리프 자체</b>가 들고 있어야 한다.
                //
                // ⚠️ 전역 iconOpacity(0.5)를 타지 않는다. 이 둘은 "지금 어디로 갈지 정하라"는 결정
                // 표식이라 반투명이면 결정을 못 한다. 다른 아이콘의 현행 톤은 건드리지 않으려고
                // 전역값을 올리는 대신 여기만 예외로 둔다.
                group.SpriteRend.sprite = null;
                group.Root.transform.localScale = Vector3.one;
                var glyph = EnsureLabel(group);
                glyph.text = glyphText;
                glyph.fontSize = glyphSize;
                glyph.color = glyphColor;
                glyph.gameObject.SetActive(true);
            }
            else
            {
                var fallback = GetFallbackSprite();
                group.SpriteRend.sprite = fallback;
                group.SpriteRend.color = WithOpacity(ResolveFallbackChipColor(icon));
                var label = EnsureLabel(group);
                label.text = ResolveFallbackShortLabel(icon);
                label.fontSize = DefaultLabelFontSize;
                label.color = WithOpacity(Color.white);
                label.gameObject.SetActive(true);
                ApplySpriteScale(group.SpriteRend, fallback);
            }
        }

        /// <summary>
        /// 이 아이콘이 <b>받침 없이 글리프만</b> 그리는 종류인가(§20-B-6 · 사용자 확정).
        ///
        /// 대상은 <c>?</c>(미판별 안전지대 후보)와 <c>!</c>(전멸기 경고) 둘이다. 공통점은 <b>둘 다
        /// 전용 스프라이트가 없는 표식</b>이면서 <b>지금 당장의 이동 결정</b>을 요구한다는 것이다 —
        /// 상태이상·넉백처럼 "이 칸에 무엇이 걸려 있다"를 알리는 정보 아이콘과 성격이 다르다.
        ///
        /// 상태이상·넉백은 여기 들어오지 않는다: 그쪽은 실제 스프라이트가 저작되어 있어 폴백 경로를
        /// 거의 타지 않고, 라벨도 글리프가 아니라 낱말("넉백")이라 받침 없이 두면 읽히지 않는다.
        /// </summary>
        private static bool TryResolveGlyphOnlyIcon(OverlayIcon icon, out string text, out Color color, out float fontSize)
        {
            // 취약 부위(§28 W4): 전용 스프라이트 발주 대기 — 그때까지 금색 '약' 글리프 폴백.
            if (icon.IsWeakSpot)
            {
                text = WeakSpotShortLabel;
                color = WeakSpotGlyphColor;
                fontSize = WeakSpotFontSize;
                return true;
            }

            if (icon.IsSafeZoneCandidate)
            {
                text = SafeZoneCandidateShortLabel;
                color = SafeZoneCandidateGlyphColor;
                fontSize = SafeZoneCandidateFontSize;
                return true;
            }

            if (icon.IsAnnihilation)
            {
                text = AnnihilationShortLabel;
                color = AnnihilationGlyphColor;
                fontSize = AnnihilationFontSize;
                return true;
            }

            text = null;
            color = default;
            fontSize = DefaultLabelFontSize;
            return false;
        }

        /// <summary>
        /// 스프라이트가 없을 때 받침 칩에 칠할 색. 저주·변위는 <see cref="StatusEffectKind"/>가 없어
        /// <c>ToColor(icon.Kind)</c>가 <c>None</c>의 색을 돌려주므로 <b>여기서 먼저 갈라야 한다</b>.
        /// </summary>
        private static Color ResolveFallbackChipColor(OverlayIcon icon)
        {
            if (icon.IsCurse)
            {
                return CurseFallbackColor;
            }

            if (icon.IsKnockback)
            {
                return icon.IsPull ? PullFallbackColor : KnockbackFallbackColor;
            }

            return ToColor(icon.Kind);
        }

        private static string ResolveFallbackShortLabel(OverlayIcon icon)
        {
            if (icon.IsCurse)
            {
                return CurseShortLabel;
            }

            if (icon.IsKnockback)
            {
                return icon.IsPull ? PullShortLabel : KnockbackShortLabel;
            }

            return ToShortLabel(icon.Kind);
        }

        private Sprite GetIconSprite(OverlayIcon icon)
        {
            // 전멸기 경고: 카탈로그에 스프라이트가 꽂히면 그것을 쓰고, 없으면 금색 '!' 글리프 폴백.
            if (icon.IsAnnihilation)
            {
                return iconCatalog != null ? iconCatalog.AnnihilationWarningSprite : null;
            }

            if (icon.IsWeakSpot)
            {
                return iconCatalog != null ? iconCatalog.WeakSpotSprite : null;
            }

            // `?`는 전용 스프라이트가 저작되면 그것을 쓰고, 없으면 글리프 폴백으로 떨어진다.
            // 아트가 오면 카탈로그에 꽂는 것만으로 교체된다(코드 변경 0).
            if (icon.IsSafeZoneCandidate)
            {
                return iconCatalog != null ? iconCatalog.SafeZoneCandidateSprite : null;
            }

            // 저주 표식(2026-09-01 W2): 아트가 오면 카탈로그에 꽂는 것만으로 교체된다(코드 변경 0).
            // 없으면 아래 폴백이 「저」 글자 칩을 그린다 — 상태이상 스프라이트로 대신 떨어뜨리지
            // 않는다(저주는 상태이상이 아니고, 남의 그림을 빌리면 어휘가 갈린다).
            if (icon.IsCurse)
            {
                return iconCatalog != null ? iconCatalog.CurseSprite : null;
            }

            return icon.IsKnockback ? GetKnockbackSprite(icon.IsPull) : GetSprite(icon.Kind);
        }

        /// <summary>
        /// 변위 표식 스프라이트. 🔴 <b>끌어당김은 밀치기로 폴백하지 않는다</b> — 방향이 반대인
        /// 그림을 대신 띄우면 화면이 거짓말을 한다. 끌어당김 아트가 아직 없으면 null을 돌려
        /// 글자 폴백(「끌」)으로 떨어뜨린다.
        /// </summary>
        private Sprite GetKnockbackSprite(bool isPull)
        {
            if (iconCatalog == null)
            {
                return null;
            }

            return isPull ? iconCatalog.PullSprite : iconCatalog.KnockbackSprite;
        }

        private TextMeshPro EnsureLabel(TileIconGroup group)
        {
            if (group.Label != null)
            {
                return group.Label;
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(group.Root.transform, false);
            labelGo.transform.localPosition = Vector3.zero;
            var label = labelGo.AddComponent<TextMeshPro>();
            label.fontSize = DefaultLabelFontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            group.Label = label;
            return label;
        }

        private void ApplySpriteScale(SpriteRenderer spriteRenderer, Sprite sprite)
        {
            if (spriteRenderer == null || sprite == null)
            {
                return;
            }

            var bounds = sprite.bounds;
            var maxDimension = Mathf.Max(bounds.size.x, bounds.size.y);
            var scale = maxDimension > 0.0001f ? iconSize / maxDimension : iconSize;
            spriteRenderer.transform.localScale = Vector3.one * scale;
        }

        private bool TryGetRaycastGroup(Camera camera, Vector2 screenPos, out TileIconGroup group)
        {
            group = null;
            if (camera == null || groups.Count == 0)
            {
                return false;
            }

            var ray = camera.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var target = hit.collider != null ? hit.collider.GetComponentInParent<StatusIconHoverTarget>() : null;
                if (target == null || target.Owner != this)
                {
                    continue;
                }

                if (TryGetGroup(target.GroupIndex, out group))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetNearestScreenGroup(Camera camera, Vector2 screenPos, out TileIconGroup group)
        {
            group = null;
            if (camera == null || groups.Count == 0)
            {
                return false;
            }

            var radius = Mathf.Max(1f, hoverScreenRadiusPixels);
            var bestDistanceSqr = radius * radius;
            for (var i = 0; i < groups.Count; i++)
            {
                var candidate = groups[i];
                if (candidate == null || candidate.Root == null)
                {
                    continue;
                }

                var viewport = camera.WorldToViewportPoint(candidate.Root.transform.position);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                {
                    continue;
                }

                var candidateScreen = (Vector2)camera.WorldToScreenPoint(candidate.Root.transform.position);
                var distanceSqr = (candidateScreen - screenPos).sqrMagnitude;
                if (distanceSqr <= bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    group = candidate;
                }
            }

            return group != null;
        }

        private bool TryGetGroup(int index, out TileIconGroup group)
        {
            if (index >= 0 && index < groups.Count)
            {
                group = groups[index];
                return group != null;
            }

            group = null;
            return false;
        }

        private Sprite GetSprite(StatusEffectKind kind)
        {
            var catalogSprite = iconCatalog != null ? iconCatalog.GetSprite(kind) : null;
            if (catalogSprite != null)
            {
                return catalogSprite;
            }

            var index = (int)kind;
            if (effectKindSprites == null || index < 0 || index >= effectKindSprites.Length)
            {
                return null;
            }
            return effectKindSprites[index];
        }

        private Sprite GetDisplaySprite(Sprite source)
        {
            if (!removeConnectedWhiteBackground || source == null)
            {
                return source;
            }

            if (whiteKeyedSpriteCache.TryGetValue(source, out var cached) && cached != null)
            {
                return cached;
            }

            var generated = CreateWhiteBackgroundRemovedSprite(source);
            whiteKeyedSpriteCache[source] = generated != null ? generated : source;
            return whiteKeyedSpriteCache[source];
        }

        private Sprite CreateWhiteBackgroundRemovedSprite(Sprite source)
        {
            if (source == null || source.texture == null)
            {
                return source;
            }

            var rect = source.textureRect;
            var width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            var height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            var copiedTexture = CopySpriteTexture(source.texture, rect, width, height);
            if (copiedTexture == null)
            {
                return source;
            }

            RemoveConnectedWhiteBackground(copiedTexture);
            return Sprite.Create(
                copiedTexture,
                new Rect(0, 0, width, height),
                new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
        }

        private static Texture2D CopySpriteTexture(Texture sourceTexture, Rect sourceRect, int width, int height)
        {
            var temporary = RenderTexture.GetTemporary(
                sourceTexture.width,
                sourceTexture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(sourceRect, 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void RemoveConnectedWhiteBackground(Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels32();
            var visited = new bool[pixels.Length];
            var queue = new Queue<int>();

            void EnqueueIfBackground(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    return;
                }
                var index = y * width + x;
                if (visited[index] || !IsRemovableBackgroundCandidate(pixels[index]))
                {
                    return;
                }
                visited[index] = true;
                queue.Enqueue(index);
            }

            for (var x = 0; x < width; x++)
            {
                EnqueueIfBackground(x, 0);
                EnqueueIfBackground(x, height - 1);
            }

            for (var y = 0; y < height; y++)
            {
                EnqueueIfBackground(0, y);
                EnqueueIfBackground(width - 1, y);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                pixels[index].a = 0;
                var x = index % width;
                var y = index / width;
                EnqueueIfBackground(x - 1, y);
                EnqueueIfBackground(x + 1, y);
                EnqueueIfBackground(x, y - 1);
                EnqueueIfBackground(x, y + 1);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }

        private bool IsRemovableBackgroundCandidate(Color32 color)
        {
            return IsWhiteBackgroundCandidate(color) || IsDarkBackgroundCandidate(color);
        }

        private bool IsWhiteBackgroundCandidate(Color32 color)
        {
            var cutoff = Mathf.RoundToInt(whiteBackgroundCutoff * 255f);
            return color.a > 0 && color.r >= cutoff && color.g >= cutoff && color.b >= cutoff;
        }

        private bool IsDarkBackgroundCandidate(Color32 color)
        {
            var cutoff = Mathf.RoundToInt(darkBackgroundCutoff * 255f);
            return color.a > 0 && color.r <= cutoff && color.g <= cutoff && color.b <= cutoff;
        }

        private Sprite GetFallbackSprite()
        {
            if (fallbackSprite != null)
            {
                return fallbackSprite;
            }
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            fallbackSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), Vector2.one * 0.5f, 4f);
            return fallbackSprite;
        }

        private void ClearAll()
        {
            foreach (var group in groups)
            {
                if (group.Root != null)
                {
                    SafeDestroy(group.Root);
                }
            }
            groups.Clear();
            ClearOrphanIconObjects();
            cycleTimer = 0f;
            // 그룹 인덱스가 전부 무효가 됐다 — 호버 얼림도 함께 푼다(#15).
            hoveredGroupIndex = -1;
            hoveredFrame = -1;
        }

        private void ClearOrphanIconObjects()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith("StatusIcon ["))
                {
                    SafeDestroy(child.gameObject);
                }
            }
        }

        private static void SafeDestroy(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private Vector3 ResolveWorldPosition(HexCoord coord)
        {
            var local = projector != null
                ? projector.ProjectOverlaySurface(coord)
                : Vector3.zero;
            local += Vector3.up * iconHeight;
            return mapSpaceRoot != null ? mapSpaceRoot.TransformPoint(local) : local;
        }

        private Quaternion ResolveCameraFacingRotation()
        {
            var cam = Camera.main != null ? Camera.main : Camera.current;
            return cam != null ? cam.transform.rotation : Quaternion.Euler(iconEulerAngles);
        }

        private Color WithOpacity(Color color)
        {
            color.a *= Mathf.Clamp01(iconOpacity);
            return color;
        }

        private Material GetIconMaterial()
        {
            if (iconMaterial != null)
            {
                return iconMaterial;
            }

            var shader = Shader.Find("SeoulPlayup/Status Icon Overlay");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            iconMaterial = new Material(shader)
            {
                name = "Status Icon Overlay Material",
                renderQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue + 50
            };
            return iconMaterial;
        }

        // Knockback fallback styling, used only when the catalog has no knockback sprite assigned.
        private static readonly Color KnockbackFallbackColor = new Color(1f, 0.55f, 0.1f, 1f);
        private const string KnockbackShortLabel = "넉백"; // 넉백

        // 끌어당김은 밀치기와 같은 축의 반대 방향이라 같은 계열이되 색이 갈려야 한다 —
        // 같은 주황이면 폴백 상태에서 두 표식을 구별할 방법이 사라진다.
        private static readonly Color PullFallbackColor = new Color(0.45f, 0.72f, 0.95f, 1f);
        private const string PullShortLabel = "끌기";
        // 저주 표식 폴백(2026-09-01 W2) — 저주 카드 프레임과 같은 계열의 자주빛.
        private static readonly Color CurseFallbackColor = new Color(0.55f, 0.24f, 0.62f, 1f);
        private const string CurseShortLabel = "저주";

        // 전멸기 경고(§13.5). 받침 없이 글리프만 뜬다(`?`와 같은 처리 · 사용자 확정).
        //
        // 색은 §17이 고른 경고 앰버를 유지한다 — 위험 해치보다 밝고 노란기가 돌아 붉은 해치 위에서
        // 뜬다. 여기서 `?`처럼 잉크로 바꾸지 않는 이유는 <b>색이 뜻을 나르기 때문</b>이다:
        // 앰버 = 위험(HUD 카운트다운과 같은 어휘), 잉크 = 중립적 미지. 둘이 같은 색이면 "피해라"와
        // "정해라"가 한 덩어리로 읽힌다.
        //
        // ⚠️ 받침이 사라지면서 색 면적이 줄었으므로 채도를 조금 올려 잡았다 — 얇은 글리프는 같은
        // 색이라도 넓은 판보다 약하게 읽힌다.
        private static readonly Color AnnihilationGlyphColor = new Color(1f, 0.76f, 0.05f, 1f);
        // 글리프는 "!"다 — 유니코드 경고 기호(U+26A0)는 DNF 폰트에 없어서 두부(□)로 떨어진다.
        private const string AnnihilationShortLabel = "!";
        private static readonly Color WeakSpotGlyphColor = new Color(1f, 0.82f, 0.25f, 1f);
        private const string WeakSpotShortLabel = "약";
        private const float WeakSpotFontSize = 5f;

        // 미판별 안전지대 후보(§20-B-6). 받침 없이 <b>글리프만</b> 크게 뜬다(사용자 확정).
        //
        // 색 제약이 <b>셋</b>이다: ⓐ 붉은 예고 해치 위에 얹히므로 적색에서 멀 것 ⓑ 후보가 함정과
        // 겹치면(§20-B-4 4~6단계) 두 아이콘이 cycleInterval 1초로 <b>번갈아</b> 뜨므로 함정 아이콘
        // 색(주황~적)에서도 멀 것 ⓒ 🔴 <b>자기가 앉는 BossSafeZoneCandidate 오버레이(청록)에서도 멀 것</b>.
        //
        // 🔴 실기 캡처가 잡은 결함: 처음엔 오버레이와 같은 청록 받침을 썼다가 <b>받침이 자기 칸 위에서
        // 통째로 사라졌다</b>. 오프라인 색 판정은 배경(인디고)과 순환 상대만 보느라 "아이콘이 자기
        // 오버레이 위에 앉는다"를 놓친다 — 상태이상 아이콘 트랙의 40px 판정이 놓쳤던 것과 같은 종류다.
        // 잉크 글리프는 청록 채움·붉은 해치 둘 다에서 뜨고, 받침이 없으니 같은 실패가 재발할 표면도 없다.
        private static readonly Color SafeZoneCandidateGlyphColor = new Color(0.04f, 0.07f, 0.17f, 1f);
        private const string SafeZoneCandidateShortLabel = "?";

        // 받침 없는 글리프의 크기. 판이 여백을 먹지 않으므로 폴백 라벨보다 크게 잡는다.
        //
        // 🔑 <b>글리프마다 다르게 잡는다.</b> 시각적 무게는 폰트 크기가 아니라 <b>잉크 면적</b>이고,
        // "!"는 같은 크기에서 "?"의 1/3쯤밖에 칠하지 않는다 — 같은 값을 주면 위험 표식이 미지 표식보다
        // 약하게 읽힌다(실기 캡처로 확인). 받침이 있던 시절에는 판이 면적을 대신 채워 이 차이가 숨어 있었다.
        private const float SafeZoneCandidateFontSize = 9f;
        // §28 W5: '!'가 잘 안 보인다는 실플레이 판정 — 15 → 22.5로 키웠다.
        private const float AnnihilationFontSize = 22.5f;

        /// <summary>판 위에 얹히는 폴백 라벨의 기본 크기(판 안에 들어가야 하므로 작다).</summary>
        private const float DefaultLabelFontSize = 3.5f;

        private static Color ToColor(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Immobilize: return new Color(0.55f, 0.75f, 1f, 1f);
                case StatusEffectKind.Poison: return new Color(0.22f, 0.95f, 0.22f, 1f);
                case StatusEffectKind.Stun: return new Color(1f, 0.86f, 0.08f, 1f);
                case StatusEffectKind.Slow: return new Color(0.18f, 0.58f, 1f, 1f);
                case StatusEffectKind.Rupture: return new Color(0.9f, 0.1f, 0.35f, 1f);
                case StatusEffectKind.Reflect: return new Color(0.72f, 0.48f, 1f, 1f);
                case StatusEffectKind.Agility: return new Color(0.1f, 0.85f, 0.85f, 1f);
                default: return new Color(1f, 0.2f, 0.12f, 1f);
            }
        }

        private static string ToShortLabel(StatusEffectKind kind) => StatusEffectInfo.DisplayName(kind);
        // --- Test hooks (internals visible to SeoulPlayup.Combat.EditModeTests) ---

        internal int ActiveIconCount => groups.Count;

        internal IReadOnlyList<int> CurrentCycleIndices => groups.Select(g => g.CurrentIndex).ToArray();

        internal void AdvanceCycleForTesting() => AdvanceCycle();

        /// <summary>에디트 모드 테스트는 프레임이 흐르지 않아 호버가 저절로 낡지 않는다 — 강제 해제 훅(#15).</summary>
        internal void ExpireHoverForTesting()
        {
            hoveredFrame = -1;
            hoveredGroupIndex = -1;
        }

        internal void SetEffectSpritesForTesting(Sprite[] sprites) => effectKindSprites = sprites;

        internal void SetIconCatalogForTesting(StatusEffectIconCatalog catalog) => iconCatalog = catalog;

        internal void SetRemoveWhiteBackgroundForTesting(bool value) => removeConnectedWhiteBackground = value;
    }

    internal sealed class StatusIconHoverTarget : MonoBehaviour
    {
        public StatusIconOverlayRenderer Owner;
        public int GroupIndex;
    }
}
