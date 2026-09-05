// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 몬스터 공격 VFX 발주 **갈래 1(설치된 에셋 팩 개조분)** 프리팹 생성기.
    /// 발주 정본 = <c>docs/design/vfx-sfx-order-sheet.md</c> §2 14규격 표(갈래 1 = 8규격),
    /// 세션 인계문 = <c>docs/prompts/vfx-import-batch1-handoff.md</c>.
    ///
    /// 왜 코드로 만드는가: 서드파티 프리팹을 손으로 복제·개조하면 "무엇을 어디서 얼마나 고쳤는지"가
    /// 프리팹 바이너리 안으로 사라진다. 여기 모아 두면 개조 내역이 diff로 읽히고, 팩이 갱신돼도
    /// 다시 돌리면 같은 결과가 나온다(멱등 — 같은 경로를 덮어쓴다).
    ///
    /// 🔴 <b>ThirdParty 원본은 절대 수정하지 않는다.</b> 원본을 인스턴스화해서 완전 언팩한 뒤
    /// 새 프리팹으로 저장할 뿐이고, 머티리얼은 <b>참조만</b> 한다(복제도 수정도 하지 않는다).
    /// 실측으로 확인한 사실이라 그렇게 할 수 있다: CFXR 우버셰이더·NAMU 마스터셰이더에는
    /// 틴트 색 프로퍼티가 아예 없고(_ShadowColor·_EdgeCol뿐), 색은 전적으로 파티클
    /// <c>startColor</c>(정점 색)에서 온다. 즉 <b>틴트의 유일한 표면이 startColor</b>다.
    ///
    /// 🔴 <b>모드 B 전방 축은 +Z다(+X 아님).</b> 런타임은
    /// <see cref="SeoulPlayup.Combat.Unity.CombatFacingUtility.ResolveHexSideRotation"/>가 돌려준
    /// <c>Quaternion.LookRotation</c>을 그대로 스폰 회전으로 쓰므로, 프리팹 로컬 +Z가 공격 방향에
    /// 얹힌다. 발주서·인계문의 "+X(동)를 전방으로 저작"은 오기다(동쪽이 6개 헥스 스냅 방향 중
    /// 하나라는 사실과 혼동된 것). 원본 팩의 자연 전방이 +Z가 아니면 <b>래퍼 자식</b>에 보정
    /// 회전을 굽는다 — 루트에 구우면 <c>Instantiate(prefab, pos, rot)</c>가 루트 회전을 덮어써서
    /// 조용히 사라진다.
    ///
    /// 🔴 <b>셀 피치는 1.732u다(인계문의 "지름 ~1.15u"는 오기).</b> 헥스 외접반지름이 1u
    /// (<c>AtlasTilePresentationView.tileRadius=1 × tileSpacing=1</c>, 출하 씬 MainGameplay 실측)라
    /// 이웃 셀 중심 거리 = √3 ≈ 1.732u다. 모드 C 1칸 모듈은 여기에 맞춰 지름 ~1.6u로 잡는다.
    ///
    /// ⚠️ 생성 프리팹의 모든 ParticleSystem은 <c>scalingMode=Hierarchy</c>로 바꾼다. 기본값
    /// <c>Local</c>은 부모 스케일을 무시해서, 래퍼에 건 크기 보정이 통째로 먹히지 않는다.
    /// </summary>
    public static class MonsterAttackVfxBuilder
    {
        private const string Folder = "Assets/Prefabs/Vfx/Combat";
        private const string MaterialFolder = Folder + "/Materials";
        private const string InkShaderName = "SeoulPlayup/Ink Particle";

        private const string SlashInk = "Assets/ThirdParty/NamuFX/Simple Stylized Slash vol2/Prefabs/Slash_Ink.prefab";
        private const string InkSplashMaterial = "Assets/ThirdParty/NamuFX/Simple Stylized Slash vol2/Materials/Ink/M_InkSplash01.mat";
        private const string FireBreath = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Fire/CFXR Fire Breath.prefab";
        private const string AoeSlash = "Assets/ThirdParty/Hovl Studio/Magic effects pack/Prefabs/AoE effects/AoE slash blue.prefab";
        private const string GroundHit = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR2 Ground Hit.prefab";
        private const string Explosion = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Explosions/CFXR Explosion 1.prefab";
        private const string GlowImpact = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR Impact Glowing HDR (Blue).prefab";
        private const string DustGround = "Assets/ThirdParty/Hovl Studio/Magic effects pack/Prefabs/Smoke effects/Dust ground.prefab";
        private const string HitRed = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR Hit A (Red).prefab";
        private const string StretchTraitMaterial = "Assets/ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Assets/Graphics/cfxr stretch trait blur add.mat";

        // ── 확정 사양(4겹)의 가족색 ───────────────────────────────────────────────────
        //
        // 🔑 C단계 재입힘으로 <b>HDR 네온 팔레트(한글 레드 ×2.2 · 텅스텐 앰버 ×2.4 …)가 통째로
        // 사라졌다.</b> 그 값들은 "가산 파티클이 블룸을 먹어 네온으로 읽히게" 하려던 것인데,
        // Q3-가가 소프트 발광을, Q6-가가 다색 액센트를 걷어 냈고, 확정 사양에서는 밝기를 색이
        // 아니라 셰이더의 <c>_RimBoost</c>·<c>_CoreBoost</c>가 낸다. 큐 하나에 가족색 한 벌
        // (림·코어·먹이 전부 여기서 파생)만 남는다 — A022의 골드 스파이크가 사라진 것도 이 때문이다.
        //
        // 🔴 <b>여기에 HDR 값(>1)을 넣지 말 것.</b> 먹 몸통이 가족색 × 0.2라서, 원료가 떠 있으면
        // 몸통까지 같이 밝아져 <b>짙은 먹</b>(Q5-나)이 성립하지 않는다.
        // 다크 키라인 색 — 출하된 글리프 표식(`?`·`!`)·상태 아이콘이 쓰는 다크 네이비 그대로다.
        // 확정 사양은 새 규칙이 아니라 이미 통과한 아트 계약의 이식이므로 값도 같아야 한다.
        private static readonly Color Keyline = Rgb("#070A12", 1f);

        /// <summary>흐르는 먹 그레인용 노이즈(팩 참조 — 복사·개조 없음). P1이 아크 그레인을
        /// <see cref="GenStrokeGrain"/>으로 바꿔 지금은 소비처가 없다 — 실플레이 판정에서
        /// 되돌릴 수 있는 동안만 남겨 둔다(판정 확정 시 제거).</summary>
        private const string GrainTexture =
            "Assets/ThirdParty/NamuFX/_CommonAssets/Images/Noise/noise11.png";

        // ── P1·P2 · 그림 정보량(Q48-가·Q49-가) — B 생성 그림의 거리장·그레인 ────────────
        //
        // 원화 = docs/design/vfx-ink-source/inkset_{stroke,splat}_01.png(유일본 — 재생성 불가).
        // tools/vfx-ink-bake/bake.py가 §2 반입 규약(실루엣 닫기 → EDT → ramp 정규화 → «채움»,
        // 비백은 그레인으로 분업)대로 굽는다. 그림 알파 직결은 금지 — 4겹 붕괴(벤치 반례 실증).
        // 🔴 여기서 바꾸는 것은 「그림」뿐이다 — 훑기·boiling·수명 수치는 계약상 무변경.
        private const string GenStrokeDf = "Assets/Art/VFX/InkMasks/ink_gen_stroke_df.png";
        private const string GenStrokeGrain = "Assets/Art/VFX/InkMasks/ink_gen_stroke_grain.png";
        private const string GenSplatDf = "Assets/Art/VFX/InkMasks/ink_gen_splat_df.png";

        // ── 스타일 축 · ⓑ 볼드&클린 참격 파일럿(Q57-가) — 아크 전용 그림 ──────────────
        //
        // 원화 = docs/design/vfx-ink-source/inkset_stroke_bold_01.png(유일본). 벤치 2R에서
        // 판정된 손잡이(_InkBlend 0.22 · _BoilStrength 0.07)와 함께 간다 — 출하가 판정
        // 재료와 갈라지면 안 된다. 🔴 강펀치 Streak는 P2 그대로 GenStrokeDf를 쓴다(파일럿은
        // 참격 한정 — Q56). 파일럿 기각 시 이 상수 셋과 아크 분기의 참조만 되돌리면 된다.
        private const string GenStrokeBoldDf = "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_df.png";
        private const string GenStrokeBoldGrain = "Assets/Art/VFX/InkMasks/ink_gen_stroke_bold_grain.png";

        // ── 스타일 축 · Q58 재판정 — ⓒ 셀/애니 카툰 완성형 파일럿으로 전환 ──────────────
        //
        // 3스타일 비교 영상 판정(2026-08-09): "완성도는 ⓒ가 가장 높다" → 파일럿을 ⓒ 완성형으로.
        // 원화 = docs/design/vfx-ink-source/inkset_stroke_cel_01.png(유일본, 닫기 3px —
        // 노치가 스타일). 손잡이 = 벤치 c_cel 열 그대로(_InkBlend 0 · _Posterize 5).
        // ⓑ 상수·원화는 기록으로 보존(기각 아님 — 차점).
        private const string GenStrokeCelDf = "Assets/Art/VFX/InkMasks/ink_gen_stroke_c_df.png";

        private static readonly Color FamilyBossRed = Rgb("#E0524E", 1f);
        private static readonly Color FamilyFireAmber = Rgb("#F08A3C", 1f);
        private static readonly Color FamilyBullAmber = Rgb("#D98C50", 1f);

        // 헥스 이웃 셀 중심 거리(= 한 칸 지름). HexAxialProjection: √3 × tileRadius.
        private const float CellPitch = 1.7320508f;

        /// <summary>이번 리빌드에서 실제로 만든 머티리얼. 남은 것은 재입힘으로 버려진 고아라 지운다.</summary>
        private static readonly HashSet<string> ProducedMaterials = new HashSet<string>();

        [MenuItem("Tools/Seoul Playup/Combat/Rebuild Monster Attack VFX (갈래 1)")]
        public static void Rebuild()
        {
            EnsureFolder(Folder);
            ProducedMaterials.Clear();

            // 🔴 형상 마스크가 없으면 재입힘이 성립하지 않는다(팩 알파로는 4겹이 안 선다) —
            // 마스크를 먼저 굽는다. 멱등이라 매번 돌려도 같은 결과다.
            VfxInkMaskBaker.BakeAll();

            // 🔴 궤적 리본도 마스크와 같은 계약이다 — 없으면 재입힘이 성립하지 않으므로 먼저 굽는다.
            VfxInkArcMeshBaker.BakeAll();

            var built = new List<string>
            {
                BuildClawSlash(),
                BuildFireBreath(),
                BuildFireBreathLong(),
                BuildPressureCross(),
                BuildFissureTile(),
                BuildFissureRingTile(),
                BuildArtilleryTile(),
                BuildRoarRipple(),
                BuildHornCharge(),
                BuildHeavyPunch(),
            };

            var pruned = PruneOrphanMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MonsterAttackVfxBuilder] rebuilt " + built.Count + " prefabs (머티리얼 "
                + ProducedMaterials.Count + "개 · 고아 " + pruned + "개 정리):\n  " + string.Join("\n  ", built));
        }

        // ── 규격 1: 발톱 참격(모드 B · A003 부채꼴 / A004 직선) ──────────────────────────
        // 먹 슬래시가 룩 레퍼런스 그 자체라 원본을 살리고, 가산 레이어만 한글 레드 네온으로 올린다.
        // Slash_Ink의 자연 전방은 −Z(출하 V008·V009가 rotationY≈180으로 쓰던 이유) → 래퍼에 180° 요.
        /// <summary>
        /// 발톱 참격 = <b>자작 클로로 교체</b>(2026-08-11 사용자 판정: 기존 먹 참격은 퀄리티 미달).
        /// 자작 클로도 절차적 생성이라 소스는 <see cref="ClawVfxBuilder"/>이고, <b>같은 프리팹
        /// 경로</b>에 굽는다 — 그래서 <c>combat_vfx_cues.csv</c>(V003·V004, A013 공유)를 손대지
        /// 않고 그림만 갈린다.
        /// 🔴 여기서 위임하는 이유: 이 목록이 "큐 프리팹의 단일 생성 지점"이라는 계약을 유지해야
        /// Rebuild 한 번으로 전부 최신이 된다. 위임을 빼면 Rebuild가 자작 클로를 조용히 덮어쓴다.
        /// 교체 전 먹 구현은 <see cref="BuildClawSlashInkSuperseded"/>에 그대로 남겼다 —
        /// Q29·Q58·Q63·Q65 판정 근거가 주석으로 붙어 있어 <b>나머지 큐의 스타일 계약 참조본</b>이다.
        /// </summary>
        private static string BuildClawSlash()
        {
            var baked = global::ClawVfxBuilder.BakePrefab();
            return baked != null ? global::ClawVfxBuilder.PrefabPath : "(자작 클로 굽기 실패)";
        }

        private static string BuildClawSlashInkSuperseded()
        {
            var content = Clone(SlashInk);

            // 스타일 축 Q63 후속(2026-08-09 사용자): "참격이랑 사방으로 터지는 이펙트가 함께
            // 재생되지 않도록" — Q58에서 이식했던 임팩트 문법(방사 속도선 6가닥 + 흰 섬광)을
            // 참격에서 떼어 낸다. 임팩트 문법 자체는 타격 큐(HeavyPunch·ArtilleryTile)에 남는다.
            // 참격은 「획 하나」만 보인다 — 한 번에 하나.

            // 🔑 Q1 확정(사용자): 보스 발톱은 붉게 가른다 — 호랑 선생의 청록 잉크와 구분되어야 한다.
            //
            // 🔴🔴 C단계에서 바뀐 것: 셰이더만 갈아 끼우던 것을 <b>형상 마스크까지</b> 우리 것으로
            // 바꾼다. NAMU 붓 텍스처는 알파가 거리장이 아니라 형상 그림이라 값이 최상단에 몰려
            // 있고, 그 위에 확정 사양을 먹이면 <b>테두리 두 겹이 설 자리가 없어 전부 심으로 칠해진다</b>
            // (실측). 붓 궤적의 형태는 우리 BrushSlash 마스크가 대신 든다 — 같은 붓형 테이퍼 호다.
            Reskin(content, "ClawSlash", FamilyBossRed, name =>
            {
                switch (name)
                {
                    // 참격 본체 — 궤적 리본으로 그린다(§14). 평면 쿼드였을 때는 여덟 프레임이
                    // 전부 같은 그림이라 "휘둘렀다"가 아니라 "떴다 사라졌다"로 읽혔다.
                    // 🔑 <b>판정 Q29-가 — 발톱 계열만 3갈래.</b> 발톱은 「한 획」이 아니라
                    // 「세 줄이 동시에 긁힌 자국」이다. 발톱이 아닌 참격(기합참·관통)은 한 갈래를
                    // 유지해야 둘의 구분이 선다.
                    case "slash_alp":
                    case "slash_add":
                        return InkSkin.ArcRibbon(VfxInkArcMeshBaker.ArcKind.ClawTriple);

                    // 먹 튀김은 형태가 없다 → 키라인 면제(Q2-나). 팩 실루엣이 오히려 맞다.
                    case "splash 1":
                    case "splash 2":
                        return InkSkin.Soft();

                    // Slash_Ink · fx는 방출이 없는 홀더라 손대지 않는다.
                    default:
                        // Q58(ⓒ) 방사 속도선 — 몇 픽셀짜리 획이라 키라인 면제(A014·A022와 동일).
                        if (name.StartsWith("Burst"))
                        {
                            return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                        }

                        return InkSkin.Keep();
                }
            });

            // 원본의 두 홀더 시스템(Slash_Ink·fx)은 dur 1 + life 5 = 6s짜리 껍데기라, 그대로 두면
            // 큐 길이가 실제 연출(≈1.3s)의 다섯 배로 잡힌다. 연출 길이 데이터가 이 값을 재므로
            // (docs/presentation-duration-data-plan.md) 눈에 안 보여도 반드시 쳐낸다.
            SetTiming(content, "Slash_Ink", duration: 0.6f, lifetime: 0.6f);
            SetTiming(content, "fx", duration: 0.6f, lifetime: 0.6f);

            // 🔑 <b>아크 수명 = 참격이 보이는 길이.</b> 훑기 구간의 합이 1이므로(위 주석 참조)
            // 「들어옴 → 머묾 → 지워짐」이 정확히 수명 안에서 끝난다 — 즉 <b>수명을 줄이면 그대로
            // 참격이 빨라지고</b>, 안 보이는 채 살아 있는 구간이 생기지 않는다.
            //
            // 팩에서 물려받은 값은 1.0s / 0.7s였고 <b>비율 0.7을 유지</b>한다 — 가산 겹이 먼저 그려지고
            // 먼저 지워지면서 나는 두께감이 이 어긋남에서 나온다(판정 Q25-가 «현행 유지»).
            // 🏁 Q42-가(R1 기준표 §3.1): 총 가시 길이 레퍼런스 0.52s 대 우리 0.28s — 획이 그려질
            // 시간을 준다. add는 비율 0.7 유지. ⚠️수명이 바뀌면 연출 길이 재베이크와 랩 모션 스트립
            // 촬영 시각 배열(0.01~0.50s)이 같이 움직여야 한다(기준표 §5 경고).
            SetTiming(content, "slash_alp", duration: 0.50f, lifetime: 0.50f);
            SetTiming(content, "slash_add", duration: 0.36f, lifetime: 0.36f);

            // (Q63 후속) 임팩트 흰 섬광도 위 방사 속도선과 같은 이유로 제거 — 참격 단독 재생.

            // cone-mid는 반경 2까지 뻗는다(≈3.5u). 먹 슬래시 메시가 1.22u라 2.6배.
            var root = Wrap(content, "MonsterAttack_ClawSlash", Quaternion.Euler(0f, 180f, 0f), 2.6f);
            return Save(root, "MonsterAttack_ClawSlash");
        }

        // ── 규격 2: 화염 브레스(모드 B · A007 cone-wide) ────────────────────────────────
        // 화염색은 "주황 일반 화염" 확정(도깨비불 기각) — 텅스텐 앰버 유지하고 연기만 먹 그을음으로.
        // CFXR Fire Breath의 콘은 이미 로컬 +Z 방출이라 보정 회전이 필요 없다.
        private static string BuildFireBreath()
        {
            // cone-wide는 거리 3까지 닿는다(≈5.2u). 원본 속도로는 1.5칸에서 끊겼다(실측 캡처).
            var content = BuildBreathContent(reach: 1.9f, pulses: 0);
            var root = Wrap(content, "MonsterAttack_FireBreath", Quaternion.identity, 1.0f);
            return Save(root, "MonsterAttack_FireBreath");
        }

        // ── 규격 2 변주: 화염 방사·장(모드 B · A020 cone-long · 3히트 다단) ─────────────
        // 사거리 4 초장 브레스라 도달 거리를 1.45배로 늘리고, 다단 3히트가 눈에 보이도록
        // 본류 불꽃에 맥동 버스트 3발을 얹는다(총량이 아니라 표현만 연타 — 패턴 CSV designerNote 참조).
        private static string BuildFireBreathLong()
        {
            // cone-long은 거리 4까지(≈6.9u) — cone-wide의 약 1.4배다.
            var content = BuildBreathContent(reach: 2.7f, pulses: 3);
            var root = Wrap(content, "MonsterAttack_FireBreathLong", Quaternion.identity, 1.0f);
            return Save(root, "MonsterAttack_FireBreathLong");
        }

        private static GameObject BuildBreathContent(float reach, int pulses)
        {
            var content = Clone(FireBreath);
            StopLooping(content);

            // Q2 판정 "부분" — 불꽃 혀는 키라인 O, 그을음은 면제. 같은 프리팹 안에서 갈리므로
            // 면제가 큐 단위가 아니라 <b>파티클 시스템 단위</b>여야 하는 대표 사례다.
            Reskin(content, "FireBreath", FamilyFireAmber, name =>
            {
                switch (name)
                {
                    // 팩의 불은 둥근 구름이라 색을 어떻게 바꿔도 카툰 화염이 안 된다 — 혀의 실루엣을 준다.
                    case "CFXR Fire Breath":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Flame);

                    // 잔불은 스트레치로 날아가는 점이라 키라인을 두르면 뭉갠다(형태가 뚜렷하지 않다).
                    case "Small flames":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);

                    case "Smoke":
                        return InkSkin.Soft();

                    default:
                        return InkSkin.Keep();
                }
            });

            foreach (var ps in content.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.duration = 0.9f;
                // 도달 거리 = startSpeed × lifetime. 길이 변주는 속도로 준다 — 스케일로 늘리면
                // 분사 굵기까지 같이 굵어져 "장(長) 브레스"가 아니라 그냥 큰 브레스가 된다.
                main.startSpeed = Scaled(main.startSpeed, reach);
            }

            // 원본 연기는 수명 1.5s라 공격 비트가 끝나고도 한참 남는다 — 한 박자 안에 걷히게.
            SetTiming(content, "Smoke", duration: 0.9f, lifetime: 0.9f);

            // 🔴 팩의 불은 <b>빌보드 + 무작위 회전</b>이다(둥근 구름이라 회전이 티가 안 났다).
            // 혀 실루엣을 물리는 순간 그 무작위 회전이 <b>사방으로 뻗은 혀 뭉치</b>가 된다(실측).
            // 스트레치로 바꿔 혀가 <b>속도 방향(=전방)</b>에 정렬되게 한다.
            var flame = Find(content, "CFXR Fire Breath");
            if (flame != null)
            {
                var flameMain = flame.main;
                flameMain.startRotation = new ParticleSystem.MinMaxCurve(0f);
                var flameRotation = flame.rotationOverLifetime;
                flameRotation.enabled = false;
                var flameRenderer = flame.GetComponent<ParticleSystemRenderer>();
                flameRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                flameRenderer.lengthScale = 2.2f;
                flameRenderer.velocityScale = 0f;
            }

            var fire = Find(content, "CFXR Fire Breath");
            if (fire != null && pulses > 0)
            {
                var emission = fire.emission;
                var bursts = new ParticleSystem.Burst[pulses];
                for (var i = 0; i < pulses; i++)
                {
                    bursts[i] = new ParticleSystem.Burst(0.05f + i * 0.3f, 14);
                }

                emission.SetBursts(bursts);
            }

            return content;
        }

        // ── 규격 3: 위압 십자(모드 B · A005 cross-far) ─────────────────────────────────
        // 피해보다 "압박"이 읽혀야 하는 패턴이라 붉은 피해 계열과 일부러 갈라 쿨 화이트–일렉트릭
        // 블루로 간다(같은 화면에서 A003·A004와 구분됨). 반경 2까지 뻗는 4방 파동.
        private static string BuildPressureCross()
        {
            var root = new GameObject("MonsterAttack_PressureCross");

            // 1순위 스펙이 "형상이 그대로 읽히는 것"이라, 원본 AoE 소용돌이를 틴트만 해서는 안 된다
            // — 실측 캡처에서 그냥 둥근 구슬로 보였고 십자가 전혀 읽히지 않았다. 팔 4개를 명시적으로
            // 세운다. cross-far 오프셋(2:0 · 0:2 · -2:0 · 0:-2)을 월드로 풀면 전방(+q) 기준 상대각이
            // 정확히 0° · 60° · 180° · 240°다 — 헥스판 십자라 직교 90°가 아니다.
            // 🔑 Q3 확정(사용자): 보스 가족 색으로 통일한다. 쿨 계열로 갈랐던 것을 되돌린다 —
            // "VFX만으로 모든 효과를 표현할 수는 없고, VFX는 완성도·몰입도를 올리는 장치"라는
            // 판단에 따라 기능 구분보다 가족 일관성을 택했다(상태 전달은 아이콘·툴팁이 맡는다).
            // 🔴 C단계에서 <b>글로우 팔(ArmGlow)을 뺐다</b>. 저건 획 뒤에 깔던 부드러운 발광
            // 덩어리인데 Q3-가가 금지한 바로 그것이고, 확정 사양에서는 같은 역할을 <b>획 자신의
            // 발광 림</b>이 맡는다. 대신 본 팔을 굵히지 않으면 획이 가늘어 보인다.
            var streak = LoadMaterial(StretchTraitMaterial);
            foreach (var yaw in new[] { 0f, 60f, 180f, 240f })
            {
                AddStreakArm(root, "Arm" + yaw, yaw, reach: 3.6f, color: Color.white, material: streak, lifetime: 0.26f, size: 0.95f, count: 6);
            }

            // 가운데 압박 펄스 — 팔이 뻗어 나가는 출발점이 있어야 "한 몸의 파동"으로 묶인다.
            var core = Clone(GlowImpact);
            StopLooping(core);
            StripLights(core);
            Attach(core, root.transform, new Vector3(0f, 0.1f, 0f), Quaternion.identity, 0.7f);

            // 🔑 Q3 확정(사용자): 보스 가족 색으로 통일한다. 확정 사양에서는 가족색이 하나뿐이라
            // (Q6-가 "가족별 1색 + 공용 먹") 앰버 액센트가 자연히 사라진다.
            Reskin(root, "PressureCross", FamilyBossRed, name =>
            {
                if (name.StartsWith("Arm"))
                {
                    return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak);
                }

                switch (name)
                {
                    case "CFXR Impact Glowing HDR (Blue)":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard);

                    // 절차적 링 셰이더는 텍스처가 아예 없어서 알파 경사를 만들 수 없다 —
                    // 우리 붓 링으로 갈아야 4겹이 선다.
                    case "Ring":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Ring);

                    // Q3-가 · 소프트 방사 글로우와 렌즈 플레어는 우리 어휘가 아니다.
                    case "Glow":
                    case "Flare":
                        return InkSkin.Remove();

                    default:
                        return InkSkin.Keep();
                }
            });

            return Save(root, "MonsterAttack_PressureCross");
        }

        // ── 규격 4: 균열 모듈(모드 C · A006 땅울림 / A018 링) ─────────────────────────
        // 모드 C 계약: 한 칸(피치 1.732u)을 덮는 단발 0.4~0.8s · 루프 금지 · 바닥 기준.
        // CFXR2 Ground Hit은 이미 바닥 지향(위로 솟는 파편 + 바닥에 눕는 링)이라 보정 회전이 없다.
        private static string BuildFissureTile()
        {
            var root = Wrap(BuildFissureContent(density: 1f), "MonsterAttack_FissureTile", Quaternion.identity, 1.45f);
            return Save(root, "MonsterAttack_FissureTile");
        }

        // ── 규격 4 변주: 성긴 균열 모듈(모드 C · A018 링 전용) ────────────────────────
        //
        // 🔴 <b>A018을 위해 프리팹을 갈랐다.</b> A018 판정은 "키라인 면제 + 밀도 감축(여백)"인데
        // A006은 "키라인 O"다. 둘이 같은 프리팹을 쓰고 있었으므로 한쪽을 고치면 다른 쪽이 같이
        // 바뀐다 — 그리고 A018은 <b>18칸</b>에 동시에 뜨는 링이라 A006(12칸)과 같은 밀도로 깔면
        // 판이 통째로 메워져 여백이 사라진다. 큐 V014가 이 프리팹을 가리키도록 CSV를 옮겼다.
        private static string BuildFissureRingTile()
        {
            var root = Wrap(BuildFissureContent(density: 0.45f), "MonsterAttack_FissureRingTile", Quaternion.identity, 1.45f);
            return Save(root, "MonsterAttack_FissureRingTile");
        }

        /// <param name="density">1 = A006 원래 밀도 · 0.45 = A018 여백판(입자 수와 잔재를 줄인다).</param>
        private static GameObject BuildFissureContent(float density)
        {
            var content = Clone(GroundHit);
            StopLooping(content);
            StripLights(content);

            SetTiming(content, "CFXR2 Ground Hit", duration: 0.35f, lifetime: 0.3f);
            SetTiming(content, "Ground ring", duration: 0.3f, lifetime: 0.25f);
            AddInkBlot(content, "InkBlot", count: Mathf.Max(1, Mathf.RoundToInt(5 * density)),
                size: 0.55f, lifetime: 0.35f, speed: 1.1f);

            // 균열 모듈인데 정작 균열선이 없었다 — 바닥을 긁는 꺾은선 두 가닥을 얹는다.
            // 이 형상은 A단계에서 "코드로 구울 수 있다"고 확인된 것이고 생성비가 0이다.
            AddGroundStroke(content, "CrackStroke",
                count: 1, size: density >= 1f ? 1.0f : 0.8f, lifetime: 0.4f);

            // 위로 솟는 파편이 원본 그대로면 옆 칸까지 삐져나온다 — 한 칸 모듈은 칸 안에서 끝나야 한다.
            var burst = Find(content, "CFXR2 Ground Hit");
            if (burst != null)
            {
                // 🔑 Q4 확정(사용자): 위로 솟는 "가시"를 바닥에 눕는 충격파로 바꾼다.
                // 원본은 콘이 +Y를 보고 있어(로컬 X=270°) 파편이 수직으로 솟았고, 나는 그걸
                // 물려받아 크기만 줄였을 뿐 방향을 저작하지 않았다 — 그래서 가시로도 안 읽히고
                // 균열이라는 형상과도 어긋났다. 원(Circle)을 지면(XZ)에 눕혀 바깥으로 방사하면
                // 스트레치 렌더가 그대로 지면을 긁는 충격파 선이 된다.
                burst.transform.localRotation = Quaternion.identity;
                var burstShape = burst.shape;
                burstShape.enabled = true;
                burstShape.shapeType = ParticleSystemShapeType.Circle;
                burstShape.radius = 0.3f;
                burstShape.arc = 360f;
                burstShape.radiusThickness = 1f;
                burstShape.rotation = new Vector3(-90f, 0f, 0f); // 원을 지면에 눕힌다.

                var burstMain = burst.main;
                burstMain.startSize = 0.16f;
                burstMain.startSpeed = new ParticleSystem.MinMaxCurve(2.2f);
                burstMain.startLifetime = 0.28f;
                burstMain.gravityModifier = 0f;

                var burstRenderer = burst.GetComponent<ParticleSystemRenderer>();
                burstRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                burstRenderer.alignment = ParticleSystemRenderSpace.World;
                // 🔴 팩의 삼각 텍스처는 몇 픽셀짜리라 30가닥을 깔아도 먼지로 보였지만, 우리 획
                // 마스크는 가닥마다 테두리를 가진 <b>그림</b>이라 같은 수를 깔면 성게가 된다
                // (실측). 수를 줄이고 길이를 낮춰야 「지면을 긁은 자국」으로 읽힌다.
                burstRenderer.lengthScale = 1.5f;
                burstRenderer.velocityScale = 0.06f;
                var burstEmit = burst.emission;
                burstEmit.rateOverTime = 0f;
                burstEmit.SetBursts(new[] { new ParticleSystem.Burst(0f, 11) });

                // A018 여백판은 충격파 선 자체를 성기게 뿌린다 — 18칸이 동시에 뜨므로
                // 칸마다 같은 밀도면 판이 통째로 메워진다.
                if (density < 1f)
                {
                    var burstEmission = burst.emission;
                    var bursts = new ParticleSystem.Burst[burstEmission.burstCount];
                    burstEmission.GetBursts(bursts);
                    for (var i = 0; i < bursts.Length; i++)
                    {
                        bursts[i].count = Mathf.Max(2f, bursts[i].count.constantMax * density);
                    }

                    burstEmission.SetBursts(bursts);
                    burstEmission.rateOverTime = Scaled(burstEmission.rateOverTime, density);
                }

                // ── §17 ④ A001 구조 이식 ───────────────────────────────────────────
                //
                // ② <b>뻗는 길이의 편차.</b> 지금까지 11가닥이 전부 lengthScale 1.5로 <b>같은 길이</b>라
                // 한 번에 핀 방사 도장이었다. 기준선은 Stretch 두 벌(4 : 10)로 길이를 갈라 놓는다.
                // 🔴 <b>수를 늘리지 않는다</b> — 원본에서 덜어 내 가른다(성게 방지, 위 주석 참조).
                var shortCount = Mathf.Max(2, Mathf.RoundToInt(8 * density));
                var longCount = Mathf.Max(1, Mathf.RoundToInt(3 * density));
                SetBurstCount(burst, shortCount);
                var longSpike = AddStretchVariant(burst, "CrackSpikeLong",
                    lengthScale: 4f, count: longCount, startDelay: 0.04f, lifetimeScale: 1.3f);
                // 기준선의 `Long spikes`처럼 없는 데서 자라 나온다.
                SetGrowth(longSpike, 0f, 1f);

                // ③ <b>계단.</b> 획은 링보다 살짝 늦다 — 링이 먼저 서고 그 위를 획이 긁고 나간다.
                // (팩이 물려 준 0.05는 우연이지 저작이 아니었다. 명시한다.)
                SetBeat(burst, 0.03f);
            }

            // ── §17 ④ 계단 + 성장 (나머지 세 모듈) ──────────────────────────────────
            //
            // 지금까지 넷이 <b>전부 지연 0</b>이라 한 프레임에 통째로 나타났다. 균열은 원래
            // 「쿵(링) → 지면을 긁고(획) → 금이 벌어지고(균열선) → 먼지가 뜬다(먹얼룩)」다.
            var groundRing = Find(content, "Ground ring");
            SetStartDelay(groundRing, 0f);
            SetGrowth(groundRing, 0.05f, 1.2f); // 링은 기준선처럼 <b>10배 이상</b> 자란다.

            var crackStroke = Find(content, "CrackStroke");
            SetStartDelay(crackStroke, 0.06f);
            SetGrowth(crackStroke, 0.2f, 1f); // 금은 <b>벌어져야</b> 금이다 — 다 그려진 채로 뜨면 도장이다.

            SetStartDelay(Find(content, "InkBlot"), 0.1f); // 먼지는 마지막.

            // 확정 사양: 지면을 긁는 선과 링은 형태가 뚜렷하므로 키라인 O(A006).
            // A018(여백판)은 판정이 "면제"라 같은 시스템도 키라인을 끈다 — 18칸이 동시에 떠서
            // 칸마다 테두리를 두르면 화면이 격자 무늬가 된다.
            var keyline = density >= 1f;
            Reskin(content, keyline ? "FissureTile" : "FissureRingTile", FamilyBossRed, name =>
            {
                switch (name)
                {
                    case "CFXR2 Ground Hit":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline);

                    // 긴 획 변주 — lengthScale 4로 늘어나므로 테두리는 면제한다(늘림이 키라인까지
                    // 같이 늘려 굵은 막대가 된다). 짧은 쪽만 키라인을 지고 길이 대비가 선다.
                    case "CrackSpikeLong":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                    case "Ground ring":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Ring, keyline);
                    case "CrackStroke":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Crack, keyline);
                    case "InkBlot":
                        return InkSkin.Soft();
                    default:
                        return InkSkin.Keep();
                }
            });

            // ⚠️ 2.4배는 실측 캡처에서 한 칸의 1.5배로 삐져나왔다(링이 startSize만으로 결정되지
            // 않는다 — 절차적 링 셰이더가 따로 부풀린다). 1.45배가 셀 폭 1.732u에 들어맞는다.
            return content;
        }

        // ── 규격 5: 착탄 모듈(모드 C · A022 파편 낙하) ────────────────────────────────
        // 원본 폭발은 한 칸에 쓰기엔 과하다(파티클 5u · 연기 수명 1.5s). 칸 크기로 줄이고
        // 꼬리를 쳐낸 뒤, 포격이라는 게 읽히도록 위에서 떨어지는 낙하 스트릭을 얹는다.
        private static string BuildArtilleryTile()
        {
            var content = Clone(Explosion);
            StopLooping(content);
            StripLights(content);

            SetTiming(content, "CFXR Explosion 1", duration: 0.35f, lifetime: 0.25f);
            SetTiming(content, "Sparks", duration: 0.1f, lifetime: 0.35f);
            SetTiming(content, "Impact small", duration: 0.35f, lifetime: 0.25f);
            SetTiming(content, "Lines", duration: 0.1f, lifetime: 0.2f);
            SetTiming(content, "Smoke", duration: 0.2f, lifetime: 0.45f);
            SetTiming(content, "Ring", duration: 0.3f, lifetime: 0.3f);
            AddFallStreak(content);

            // ── §17 ④ A001 구조 이식 ───────────────────────────────────────────────
            //
            // 🔴 <b>실측이 잡아낸 순서 결함부터.</b> 낙하 스트릭은 로컬 y=6에서 속도 34로 떨어지니
            // 바닥에 닿는 시각이 <b>0.176초</b>인데, 폭발 다발은 0.05초에 터지고 있었다 —
            // 즉 <b>포탄이 도착하기 전에 폭발이 끝나 있었다.</b> 계단을 짜기 전에 이걸 맞춘다.
            // (Stretch 길이는 <c>size × lengthScale</c>이라 <b>속도와 무관</b>하다 — 속도를 올려도
            // 획이 짧아지지 않는다. 그래서 큐 길이를 늘리는 대신 낙하를 빠르게 한다.)
            var fall = Find(content, "FallStreak");
            if (fall != null)
            {
                var fallMain = fall.main;
                fallMain.startSpeed = new ParticleSystem.MinMaxCurve(100f); // 6 / 100 = 0.06초에 착탄.
                fallMain.startLifetime = 0.08f;
                fallMain.duration = 0.2f;
            }

            // ③ 계단 — 「떨어진다(0) → 착탄·링·불티(0.06) → 이차 폭발(0.11) → 연기(0.16)」.
            // 팩이 물려 준 0.05/0.05/0.1은 저작이 아니라 원본 잔재였고, 위 결함 때문에 전부 어긋나 있었다.
            const float Impact = 0.06f;
            SetBeat(Find(content, "FallStreak"), 0f);
            SetBeat(Find(content, "CFXR Explosion 1"), Impact);
            SetBeat(Find(content, "Sparks"), Impact);
            SetBeat(Find(content, "Lines"), Impact);
            SetBeat(Find(content, "Ring"), Impact);
            SetBeat(Find(content, "Impact small"), Impact + 0.05f);
            SetBeat(Find(content, "Smoke"), Impact + 0.1f);

            // ① 성장 — 셋 다 "이미 절반 넘게 커진 채로" 나타나고 있었다(0.6 / 0.5 / 0.3 → 1).
            // 기준선의 링은 0.1 → 1이고 중심 버스트는 0.75 → <b>1.5</b>로 끝까지 자란다.
            SetGrowth(Find(content, "CFXR Explosion 1"), 0.45f, 1.15f);
            SetGrowth(Find(content, "Impact small"), 0.35f, 1.15f);
            SetGrowth(Find(content, "Ring"), 0.12f, 1.2f);

            // ② 뻗는 길이의 편차 — 불티 50가닥이 전부 lengthScale 1이라 균일한 알갱이 구름이었다.
            // 50에서 10을 덜어 내 길게 뻗는 형제로 돌린다(총 수 불변).
            var sparks = Find(content, "Sparks");
            SetBurstCount(sparks, 40);
            var sparksLong = AddStretchVariant(sparks, "SparksLong",
                lengthScale: 5f, count: 10, startDelay: Impact + 0.02f, lifetimeScale: 1.25f);
            SetGrowth(sparksLong, 0f, 1f);

            // R4(Q39 확정) — 착탄 순간의 방사 속도선. 🔴content의 자식이어야 Wrap에 실려 간다
            // (형제로 두면 저장에서 조용히 사라진다 — AddStretchVariant에서 밟은 함정).
            var burstStreak = LoadMaterial(StretchTraitMaterial);
            foreach (var yaw in new[] { 0f, 60f, 120f, 180f, 240f, 300f })
            {
                AddStreakArm(content, "Burst" + yaw, yaw, reach: 2.2f, color: Color.white,
                    material: burstStreak, lifetime: 0.22f, size: 0.45f, count: 3, delay: Impact);
            }

            // 🔑 Q6-가 확정: 가족별 1색 + 공용 먹이고 <b>골드는 보스·경고 전용</b>이라
            // A022의 골드 스파이크가 교체 대상으로 지목됐다. 확정 사양에서는 색이 가족색 한 벌로
            // 좁혀지므로(림·코어·먹이 전부 같은 색에서 나온다) 앰버·골드 액센트가 구조적으로 사라진다.
            Reskin(content, "ArtilleryTile", FamilyBossRed, name =>
            {
                switch (name)
                {
                    // 폭발 다발과 소형 임팩트 — 각진 파편이 카드 일러의 임팩트 어휘다.
                    case "CFXR Explosion 1":
                    case "Impact small":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard);

                    // 낙하 스트릭은 "떨어졌다"를 읽히게 하는 획이라 형태가 뚜렷하다 → 키라인 O.
                    case "FallStreak":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak);

                    // 불티·충격선은 한 칸 안에서 몇 픽셀짜리라 키라인을 두르면 뭉갠다.
                    // (`SparksLong`은 lengthScale 5로 늘어나는 형제라 더더욱 면제다 — §17 ④.)
                    case "Sparks":
                    case "SparksLong":
                    case "Lines":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);

                    // R4 방사 속도선 — Sparks와 같은 이유로 키라인 면제.
                    case "Burst0":
                    case "Burst60":
                    case "Burst120":
                    case "Burst180":
                    case "Burst240":
                    case "Burst300":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);

                    case "Ring":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Ring);

                    case "Smoke":
                        return InkSkin.Soft();

                    default:
                        return InkSkin.Keep();
                }
            });

            // 최대 파티클 5u → 칸 크기(≈1.6u)로 0.32배.
            // R4(Q44-가) — 착탄 섬광. Reskin 뒤·Wrap 앞(content의 자식이어야 저장에 실린다).
            AddImpactFlash(content, "ImpactFlash", new Vector3(0f, 0.3f, 0f), size: 3.2f, delay: Impact);

            var root = Wrap(content, "MonsterAttack_ArtilleryTile", Quaternion.identity, 0.32f);
            return Save(root, "MonsterAttack_ArtilleryTile");
        }

        // ── 규격 6: 포효 파문(모드 A · self · A029 강화 포효) ────────────────────────
        //
        // 🔴🔴 <b>이 규격만 개조가 아니라 재제작이다.</b> 원본 CFXR Impact Glowing의 붉은 방사는
        // 알파가 전면 1이고 RGB도 가장자리까지 중간 밝기라, 어떤 표면 처리를 걸어도 <b>사각 판이
        // 남는다</b>(A단계에서 세 번 실측했다). 즉 Q3-가 "소프트 방사 글로우 금지"는 이 큐에 한해
        // 스위치가 아니라 <b>다시 만드는 일</b>이었다. 팩 프리팹을 통째로 버리고 우리 붓 링만으로
        // 세운다 — 파문은 원래 링이지 글로우가 아니다.
        //
        // 피해 0의 자기 강화라 "때린다"가 아니라 "부풀어 오른다"로 읽혀야 한다.
        private static string BuildRoarRipple()
        {
            var root = new GameObject("MonsterAttack_RoarRipple");

            // 동심원 3겹 — 0.18s 간격으로 터지고 각자 커지며 옅어진다. 링이 붓 링(가장자리가
            // 끊기고 굵기가 불균일)이라 등폭 완전원이 주던 스티커 느낌이 사라진다.
            var ripple = AddGroundStroke(root, "Ripple",
                count: 1, size: 2.4f, lifetime: 0.45f);
            var rippleMain = ripple.main;
            rippleMain.duration = 0.9f;
            var rippleEmission = ripple.emission;
            rippleEmission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 1),
                new ParticleSystem.Burst(0.18f, 1),
                new ParticleSystem.Burst(0.36f, 1),
            });

            // 파문이려면 <b>커져야</b> 한다 — 크기 곡선이 이 연출의 본체다.
            // 🔑 §17 ④: 0.35 → 1은 "이미 3분의 1쯤 커진 채로 나타난다"라 파문으로 안 읽혔다.
            // 기준선 A001의 링은 <b>0.1 → 1</b>이다. 같은 폭으로 벌린다.
            //
            // 🏁 R1 §3.3(Q46-가): 확장은 <b>0.35s에 끝나야</b> 한다(레퍼런스 fan 0.33 · tornado 0.37).
            // 우리는 창 끝까지 계속 자라 「릴리스가 아예 없다 · 끝에서 가장 밝다」 결함이 실측됐다.
            // 수명 0.45s = 어택 0.35(곡선 78%에서 정점) + 릴리스 0.10(알파가 지운다).
            // 알파 키는 여기서 저작한다 — <see cref="ApplyLifetimeRamp"/>가 알파 키를 보존하므로
            // Reskin을 지나도 릴리스가 살아남는다.
            var rippleGrow = new AnimationCurve(
                new Keyframe(0f, 0.12f / 1.15f), new Keyframe(0.78f, 1f), new Keyframe(1f, 1f));
            var rippleSol = ripple.sizeOverLifetime;
            rippleSol.enabled = true;
            rippleSol.size = new ParticleSystem.MinMaxCurve(1.15f, rippleGrow);
            var rippleFadeGradient = new Gradient();
            rippleFadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.78f),
                    new GradientAlphaKey(0f, 1f),
                });
            var rippleCol = ripple.colorOverLifetime;
            rippleCol.enabled = true;
            rippleCol.color = new ParticleSystem.MinMaxGradient(rippleFadeGradient);

            // 부풀어 오르는 방향성 — 위로 솟는 먹 조각. 자기 강화라 몸 언저리를 벗어나면 안 된다.
            var rise = AddGroundStroke(root, "Rise",
                count: 5, size: 0.4f, lifetime: 0.5f);
            rise.transform.localRotation = Quaternion.identity; // 눕히지 않고 세워 둔다(상승이므로).
            var riseMain = rise.main;
            riseMain.duration = 0.6f;
            riseMain.startSpeed = new ParticleSystem.MinMaxCurve(1.3f);
            var riseShape = rise.shape;
            riseShape.enabled = true;
            riseShape.shapeType = ParticleSystemShapeType.Cone;
            riseShape.angle = 8f;
            riseShape.radius = 0.35f;
            riseShape.rotation = new Vector3(-90f, 0f, 0f); // 위로.
            var riseRenderer = rise.GetComponent<ParticleSystemRenderer>();
            riseRenderer.alignment = ParticleSystemRenderSpace.View;

            // §17 ④ ③ — 중심(솟는 조각)은 링보다 <b>늦게</b> 온다. 기준선의 중심 버스트가 0.10초에
            // 오는 것과 같은 자리다. 조각도 자라야 "부풀어 올랐다"가 된다.
            SetStartDelay(rise, 0.05f);
            SetGrowth(rise, 0.55f, 1.15f);

            // §17 ④ ② — 뻗는 길이의 편차. 기준선은 Stretch 두 벌(4 : 10)로 이걸 만든다.
            // 솟는 조각(5)에서 2를 덜어 내 획으로 돌린다 — 총 입자 수는 그대로다.
            SetBurstCount(rise, 3);
            var roarShort = AddStretchVariant(rise, "RoarSpikeShort", lengthScale: 4f, count: 4, startDelay: 0f);
            var roarLong = AddStretchVariant(rise, "RoarSpikeLong",
                lengthScale: 10f, count: 2, startDelay: 0.05f, lifetimeScale: 1.3f);
            // 기준선의 `Long spikes`는 SoL 0 → 1이라 <b>없는 데서 자라 나온다</b>.
            SetGrowth(roarLong, 0f, 1f);

            // 🔴🔴 <b>솟는 조각의 콘(위쪽 8°)을 그대로 물려받으면 획이 화면에서 사라진다.</b>
            // Stretch는 <b>속도 방향으로</b> 늘어나는데, 이 게임 카메라는 위에서 내려다보므로
            // 위로 뻗는 획은 <b>정면으로 서서(end-on) 몇 픽셀로 눌린다</b> — 실측 스트립에서 길이
            // 편차가 전혀 안 읽힌 이유가 이것이었다(입자는 정상 방출되고 있었다).
            // 기준선 A001의 스파이크가 <b>화면 안에서 방사</b>로 보이는 것은 그것이 카메라 평면에
            // 눕기 때문이다. 그래서 획만 <b>지면(XZ)으로 눕혀 바깥으로</b> 쏜다.
            foreach (var spike in new[] { roarShort, roarLong })
            {
                if (spike == null)
                {
                    continue;
                }

                var spikeShape = spike.shape;
                spikeShape.enabled = true;
                spikeShape.shapeType = ParticleSystemShapeType.Circle;
                spikeShape.radius = 0.3f;
                spikeShape.radiusThickness = 1f;
                spikeShape.arc = 360f;
                spikeShape.rotation = new Vector3(-90f, 0f, 0f); // 원을 지면에 눕힌다(획이 바깥으로 뻗는다).
                var spikeMain = spike.main;
                spikeMain.startSpeed = new ParticleSystem.MinMaxCurve(2.4f);
            }

            Reskin(root, "RoarRipple", FamilyBossRed, name =>
            {
                switch (name)
                {
                    case "Rise":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard);

                    // 뻗는 획은 Stretch로 길이 방향으로 늘어난다 — 테두리를 두르면 그 늘림이
                    // 키라인까지 같이 늘려 굵은 막대가 된다(A015·A022의 불티를 면제로 둔 것과 같은 이유).
                    case "RoarSpikeShort":
                    case "RoarSpikeLong":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);

                    default:
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Ring);
                }
            });

            return Save(root, "MonsterAttack_RoarRipple");
        }

        // ── 규격 7: 뿔 돌진(모드 B · A015 line-2 + 넉백 2칸) ─────────────────────────
        // 황소는 저음·무게 담당이라 네온을 아끼고 먼지를 앞세운다. 먼지 궤적(+Z로 뻗음) +
        // 착탄 지점 앰버 플래시. 원본 Dust ground는 반경 6의 광역 루프라 그대로 쓰면 판을 덮는다.
        /// <summary>
        /// 납작한 쿼드의 <b>가로세로 비를 제한</b>한다(판정 Q32-가).
        ///
        /// <para>🔴🔴 <b>「종이조각」의 정체는 마스크가 아니라 이 비율이었다.</b> 팩의 임팩트 시스템은
        /// 3D 시작 크기가 <c>X 2~3 / Y 0.2~0.5</c> = <b>6:1</b>이라, 어떤 마스크를 물려도
        /// <b>세로로 1/6로 눌린다</b>(파편 마스크는 두께 28.5% → 4.8%). 수명 중에도 X는 5배,
        /// Y는 2배로 자라 시간이 갈수록 더 납작해진다. <b>마스크를 다시 그리거나 발주해도 소용없다.</b></para>
        ///
        /// <para>⚠️ <b>「파편만 두툼하게, 획은 속도선이니 그대로」로 가르면 안 된다</b> — 실제로 그렇게
        /// 갈라 봤더니 파편 쪽을 세로 5배로 키워도 <b>화면의 0.33%</b>밖에 안 바뀌었다.
        /// 화면을 지배하는 것은 <c>Impact</c> 획 시스템 쪽이다. 그래서 <b>비율로 일괄</b>한다.</para>
        ///
        /// <para>세로만 키운다 — 가로를 줄이면 뻗는 거리가 짧아져 임팩트의 덩치가 사라진다.</para>
        /// </summary>
        private static int LimitAspectRatio(GameObject root, float maxRatio)
        {
            var touched = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (!main.startSize3D)
                {
                    continue;
                }

                var x = Mathf.Max(main.startSizeX.constantMax, main.startSizeX.constant);
                var y = Mathf.Max(main.startSizeY.constantMax, main.startSizeY.constant);
                if (y < 0.0001f || x / y <= maxRatio)
                {
                    continue;
                }

                var scale = (x / maxRatio) / y;
                var sy = main.startSizeY;
                sy.constant *= scale;
                sy.constantMin *= scale;
                sy.constantMax *= scale;
                main.startSizeY = sy;
                touched++;
            }

            return touched;
        }

        private static string BuildHornCharge()
        {
            var root = new GameObject("MonsterAttack_HornCharge");

            var dust = Clone(DustGround);
            StopLooping(dust);
            var dustSystem = dust.GetComponent<ParticleSystem>();
            if (dustSystem != null)
            {
                // ⚠️ 실측 캡처에서 원본 설정은 방향이 전혀 없는 둥근 구름으로 보였다. 원인 둘:
                // 반경 6짜리 광역 콘이고(칸 개념이 없다) 셰이프가 위(-90°)를 보고 있었다.
                // 좁은 콘을 전방(+Z)으로 눕히고 크기를 줄여야 "지나간 자리"가 된다.
                var shape = dustSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.radius = 0.3f;
                shape.angle = 12f;
                shape.rotation = Vector3.zero;
                var main = dustSystem.main;
                main.duration = 0.45f;
                main.startLifetime = 0.45f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(7.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.1f);
                var emission = dustSystem.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            }

            Attach(dust, root.transform, new Vector3(0f, 0.15f, 0f), Quaternion.identity, 1f);

            // 먼지만으로는 돌진의 속도가 안 보인다 — 앞으로 찢고 나가는 앰버 잔상 궤적을 얹는다.
            var streak = LoadMaterial(StretchTraitMaterial);
            foreach (var yaw in new[] { -7f, 0f, 7f })
            {
                AddStreakArm(root, "ChargeTrail" + yaw, yaw, reach: CellPitch * 2f, color: Color.white, material: streak, lifetime: 0.2f, size: 0.45f, count: 4);
            }

            var flash = Clone(HitRed);
            StopLooping(flash);
            StripLights(flash);
            // line-2의 끝(2칸 앞)에서 터져야 "밀어냈다"가 읽힌다. 먼지가 도달한 뒤에 터지도록 늦춘다.
            foreach (var ps in flash.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startDelay = 0.14f;
            }

            Attach(flash, root.transform, new Vector3(0f, 0.2f, CellPitch * 2f), Quaternion.identity, 0.9f);

            // 🔑 Q2 판정: A015는 <b>키라인 전면 면제</b>다. 황소는 저음·무게 담당이라 이 큐의
            // 주인공이 먼지이고, 먼지에 테두리를 두르면 먼지가 아니라 돌덩이가 된다.
            // 궤적·플래시도 같은 큐 안에서 톤이 갈리면 안 되므로 함께 면제로 간다.
            Reskin(root, "HornCharge", FamilyBullAmber, name =>
            {
                if (name.StartsWith("ChargeTrail"))
                {
                    return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                }

                switch (name)
                {
                    case "Dust ground":
                        return InkSkin.Soft();
                    case "Impact spikes":
                    case "CFXR Hit A (Red)":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard, keyline: false);
                    case "Lines":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                    default:
                        // Impact 90-180 / 180-270 / 270-360 = 팩의 사분면 부채꼴.
                        // 🔴 여기에 파편 마스크를 물리면 <b>방사형 다발 위에 방사형 다발</b>이라
                        // 어두운 반죽이 되고(실측), 아예 빼면 임팩트의 덩치가 통째로 사라진다
                        // (그것도 실측했다). 획 마스크면 부채꼴이 <b>뻗어 나가는 선</b>이 되어
                        // 덩치는 남기고 뭉침만 없앤다.
                        return name.StartsWith("Impact ")
                            ? InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false)
                            : InkSkin.Keep();
                }
            });

            // 판정 Q32-가 — 6:1 납작 쿼드를 3:1로 완화한다(위 주석 참조).
            LimitAspectRatio(root, 3f);

            return Save(root, "MonsterAttack_HornCharge");
        }

        // ── 규격 8: 강펀치(모드 B · A014 line-3 관통) ────────────────────────────────
        // 3칸 직선이 형상 그대로 읽혀야 한다 — 임팩트를 칸마다 하나씩, 앞으로 갈수록 늦게 터뜨려
        // 주먹이 관통해 나가는 잔상을 만든다. 만화적 과장이 M005(깡패 돼지)의 결이다.
        private static string BuildHeavyPunch()
        {
            var root = new GameObject("MonsterAttack_HeavyPunch");

            for (var i = 0; i < 3; i++)
            {
                var hit = Clone(HitRed);
                StopLooping(hit);
                StripLights(hit);

                foreach (var ps in hit.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startDelay = 0.06f * i;
                }

                hit.name = "Punch" + (i + 1);
                // 0.7배는 칸 대비 너무 작아 안 읽혔고 1.15배는 서로 겹쳐 뭉갰다(둘 다 실측).
                // 0.9배면 임팩트 하나가 칸 하나를 채우면서 옆 임팩트와 붙지 않는다.
                Attach(hit, root.transform, new Vector3(0f, 0.2f, CellPitch * (i + 1)), Quaternion.identity, 0.9f);
            }

            // R4(Q39 확정) — 타격 순간의 방사 속도선. 첫 펀치 칸에서 여섯 방향으로 뻗는다.
            // Reskin 앞에 세워야 규칙이 획 마스크를 입힌다(PressureCross의 Arm과 같은 경로).
            var burstHolder = new GameObject("BurstArms");
            burstHolder.transform.SetParent(root.transform, false);
            burstHolder.transform.localPosition = new Vector3(0f, 0f, CellPitch);
            var burstStreak = LoadMaterial(StretchTraitMaterial);
            foreach (var yaw in new[] { 0f, 60f, 120f, 180f, 240f, 300f })
            {
                AddStreakArm(burstHolder, "Burst" + yaw, yaw, reach: 2.4f, color: Color.white,
                    material: burstStreak, lifetime: 0.22f, size: 0.5f, count: 3);
            }

            // A014는 키라인 O — 만화적 임팩트라 각진 파편에 다크 키라인을 두르는 것이 정확히
            // 카드 일러의 어휘다. 충격선(Lines)만 두께가 몇 픽셀이라 면제로 둔다.
            Reskin(root, "HeavyPunch", FamilyBossRed, name =>
            {
                switch (name)
                {
                    case "Lines":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                    case "Impact spikes":
                        return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard);
                    default:
                        if (name.StartsWith("Punch"))
                        {
                            return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Shard);
                        }

                        // R4 방사 속도선 — 몇 픽셀짜리 획이라 Lines와 같은 이유로 키라인 면제.
                        if (name.StartsWith("Burst"))
                        {
                            return InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak, keyline: false);
                        }

                        // 사분면 부채꼴 — HornCharge와 같은 이유로 획 마스크를 준다. 다만 A014는
                        // 키라인 O이므로 여기서는 테두리를 두른다.
                        return name.StartsWith("Impact ")
                            ? InkSkin.Shape(VfxInkMaskBaker.InkMaskKind.Streak)
                            : InkSkin.Keep();
                }
            });

            // 판정 Q32-가 — 6:1 납작 쿼드를 3:1로 완화한다(<see cref="LimitAspectRatio"/> 참조).
            LimitAspectRatio(root, 3f);

            // R4(Q44-가) — 흰 섬광. Reskin 뒤에 와야 전용 머티리얼이 안 덮인다(메서드 주석 참조).
            AddImpactFlash(root, "ImpactFlash", new Vector3(0f, 0.25f, CellPitch), size: 2.6f);

            return Save(root, "MonsterAttack_HeavyPunch");
        }

        // ── 확정 사양(4겹) 재입힘 ───────────────────────────────────────────────────
        //
        // 2026-08-07 A단계 판정 Q1~Q7의 구현체다. 갤러리 정본 = docs/prompts/vfx-style-unification-handoff.md §9.

        /// <summary>파티클 시스템 하나를 어떻게 재입힐지. <b>큐가 아니라 시스템 단위</b>가 판정이다
        /// (Q2-나) — 같은 프리팹 안에서도 형태가 뚜렷한 것만 키라인을 두르고 연기·먼지는 면제한다.</summary>
        private readonly struct InkSkin
        {
            public readonly VfxInkMaskBaker.InkMaskKind? Mask;
            public readonly VfxInkArcMeshBaker.ArcKind? Arc;
            public readonly bool Keyline;
            public readonly bool Drop;
            public readonly bool Skip;

            private InkSkin(
                VfxInkMaskBaker.InkMaskKind? mask,
                bool keyline,
                bool drop,
                bool skip,
                VfxInkArcMeshBaker.ArcKind? arc = null)
            {
                Mask = mask;
                Arc = arc;
                Keyline = keyline;
                Drop = drop;
                Skip = skip;
            }

            /// <summary>
            /// <b>궤적 리본 메시</b>로 그린다 — 참격·파문처럼 <b>휘둘러 지나가는</b> 연출 전용.
            ///
            /// <para>🔑 형상을 메시가 들고 텍스처는 <b>단면만</b> 든다
            /// (<see cref="VfxInkMaskBaker.InkMaskKind.Band"/>). 그 위에 셰이더의 훑기가 걸려
            /// 리본을 <b>길이 방향으로 그려 나갔다 지운다</b> — 그게 잔상이다.
            /// 평면 쿼드로는 이 셋 중 무엇도 성립하지 않는다(§14).</para>
            /// </summary>
            public static InkSkin ArcRibbon(VfxInkArcMeshBaker.ArcKind arc, bool keyline = true) =>
                new InkSkin(VfxInkMaskBaker.InkMaskKind.Band, keyline, false, false, arc);

            /// <summary>우리가 구운 형상 마스크로 갈아 끼운다 — 4겹이 실제로 서는 유일한 경우다.</summary>
            public static InkSkin Shape(VfxInkMaskBaker.InkMaskKind mask, bool keyline = true) =>
                new InkSkin(mask, keyline, false, false);

            /// <summary>팩 텍스처를 유지한 채 먹 셰이더만 씌운다. <b>키라인 면제 시스템 전용</b> —
            /// 연기·먼지·얼룩은 애초에 테두리가 없어야 하므로 거리장이 필요 없고, 팩의 뭉게진
            /// 실루엣이 오히려 맞다. 형태가 뚜렷해야 하는 시스템에 이걸 쓰면 <b>겹이 안 선다.</b></summary>
            public static InkSkin Soft() => new InkSkin(null, false, false, false);

            /// <summary>시스템 자체를 없앤다 — Q3-가(소프트 방사 글로우·렌즈 플레어 금지).</summary>
            public static InkSkin Remove() => new InkSkin(null, false, true, false);

            /// <summary>손대지 않는다(빈 홀더 시스템 등).</summary>
            public static InkSkin Keep() => new InkSkin(null, false, false, true);
        }

        private delegate InkSkin SkinRule(string systemName);

        /// <summary>
        /// 확정 사양 = <b>바깥부터 [발광 림 · 다크 키라인 · 짙은 먹 몸통 · 네온 코어]</b> 4겹.
        /// 이 값 한 벌이 A단계 판정의 전부다 — 갈래 1처럼 프리팹마다 수치가 흩어지면 안 되므로
        /// <b>여기가 유일한 출처</b>이고, 비교 벤치(<c>VfxStyleVariantLab</c>)도 이 함수를 부른다.
        ///
        /// <para>🔴 <b>단수가 곧 겹 두께다.</b> 한 겹은 단면의 <c>1/_Posterize</c>를 차지한다.
        /// 4단이면 키라인 혼자 25%를 먹어 <b>테두리가 아니라 채움</b>이 되고 그림이 속 빈 윤곽선으로
        /// 읽힌다(실측). 8단이라야 림·키라인 각 12.5% · 몸통 62.5%로 「덩어리를 두른 선」이 된다.</para>
        ///
        /// <para>🔴 <b>먹을 순검정으로 두지 말 것.</b> 지면이 야경이라 몸통이 배경과 붙어 사라진다
        /// → 가족색을 0.2로 눌러 쓴다.</para>
        /// </summary>
        /// <summary>
        /// 키라인 면제 시스템(연기·먼지·먹 얼룩)의 표면. <b>4겹을 걸지 않는다.</b>
        ///
        /// <para>🔴 <b>면제를 "키라인만 끈 확정 사양"으로 구현하면 안 된다</b> — 실측으로 밟았다.
        /// 발광 림과 네온 코어는 <b>실루엣 경계와 중심이 있는 형태</b>를 전제하는데 연기 텍스처에는
        /// 그런 게 없어서, 뭉게구름 전체가 림 색으로 칠해지고 8단 포스터라이즈가 그걸 딱딱한
        /// 덩어리로 끊어 <b>불투명한 분홍 반죽</b>이 됐다(A022·A015·A003 셋 다). 면제 시스템이
        /// 맡은 일은 형태가 아니라 <b>공기</b>이므로, 여기서는 먹으로 가라앉히기만 한다.</para>
        /// </summary>
        private static void ApplyExemptWash(Material m, Color family)
        {
            // 계단은 얕게 — 연기를 8단으로 끊으면 구름이 아니라 판자가 된다.
            m.SetFloat("_Posterize", 3f);
            m.SetFloat("_AlphaCut", 0.06f);

            // 몸통은 거의 먹으로. 공기는 색을 주장하면 안 된다.
            m.SetColor("_InkColor", new Color(family.r * 0.18f, family.g * 0.18f, family.b * 0.18f, 1f));
            m.SetFloat("_InkBlend", 0.82f);

            // 🔴 <b>선명함(Q28-가)은 면제 계열에 걸지 않는다 — 명시적으로 되돌린다.</b>
            // <c>_OpaqueFrom</c>은 실루엣 안쪽을 불투명하게 만들어 「밝은 면 + 어두운 윤곽」을
            // 세우는 장치인데, 연기에는 세울 실루엣이 없다. 여기에 걸면 뭉게구름이 <b>불투명한
            // 덩어리</b>가 되어 위 주석이 경고하는 「분홍 반죽」을 불투명도 쪽에서 다시 만든다.
            // 기본값 1 = 옛 동작(겹이 곧 불투명도)이고, 공기는 그게 맞다.
            m.SetFloat("_OpaqueFrom", 1f);

            m.SetFloat("_RimWidth", 0f);
            m.SetFloat("_CoreWidth", 0f);
            m.SetFloat("_EdgeWidth", 0f);
            m.SetFloat("_EdgeBoost", 1f);
            m.SetFloat("_RimBoost", 1f);
            m.SetFloat("_CoreBoost", 1f);

            // 🔴 boiling도 면제 계열에는 걸지 않는다 — <c>_OpaqueFrom</c>과 같은 이유로 명시적으로
            // 되돌린다. boiling은 형상 마스크의 실루엣을 다시 그리는 장치인데 연기는 팩의 부드러운
            // 텍스처(형상 없음)를 쓰므로, 여기 걸면 공기가 10fps로 덜컹거리기만 한다.
            // 필요해지면 판정을 거쳐 약하게 확대한다(R1 이후).
            m.SetFloat("_BoilStrength", 0f);

            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        public static void ApplyConfirmedSpec(Material m, Color family, bool keyline)
        {
            m.SetFloat("_Posterize", 8f);
            m.SetFloat("_AlphaCut", 0.04f);

            // 🔴🔴 <b>판정 Q28-가 · Q31-가 — 「탁하다」의 구조적 원인을 전 큐에 걷어낸다.</b>
            //
            // 원인은 채도가 아니었다. 실측하면 우리 채도는 팩보다 <b>높은데</b>(0.80 대 0.04~0.26)
            // 「밝으면서 채도도 살아 있는 빨강」이 <b>1.1%</b>뿐이었다 — 화면이 「어두운 마룬」과
            // 「흰 심」으로만 갈라지고 그 사이가 통째로 비어 있었다.
            //
            // 범인은 <c>_OpaqueFrom = 1</c>이다. 출력 불투명도가 겹 판정값(<c>banded</c>)에 묶여
            // <b>바깥 겹일수록 반투명</b>하게 찍히고, 어두운 배경과 섞이며 <b>중간 밝기 램프</b>가 생긴다.
            // 🔑 <b>몸통 색만 어둡게 한 안은 그 값이 0.0%였다 — 색으로는 못 고친다는 증거다.</b>
            //
            // 그래서 둘을 같이 건다: 실루엣 안쪽을 불투명하게(<c>_OpaqueFrom</c>) + 먹 몸통을
            // 「짙은 마룬」이 아니라 <b>선명한 가족색 쪽으로</b>(<c>_InkColor</c>·<c>_InkBlend</c>).
            // ⚠️ 계단을 4단까지 내리는 안은 <b>채도가 0.58로 떨어져</b> 기각했다(색이 흰색으로 날아간다).
            // 🔑 R1 정정(기준표 §4.3, Q43-가): 모자란 것은 채도가 아니라 <b>먹(무채색)</b>이다 —
            // 레퍼런스 무채색 10~18% 대 우리 0.5~11%(채도는 우리가 더 높다: 0.69~0.78 대 0.47~0.58).
            // 가족색 그대로의 어두운 몸통은 「어둠」으로는 세지만 「무채색」으로는 안 센다(S<0.20 기준).
            // 그래서 먹 몸통을 회색 쪽으로 절반 넘게 눕힌다 — 수묵의 먹이 원래 무채색이다.
            var inkGray = (family.r + family.g + family.b) / 3f;
            var inkBase = Color.Lerp(family, new Color(inkGray, inkGray, inkGray, 1f), 0.55f);
            m.SetColor("_InkColor", new Color(inkBase.r * 0.45f, inkBase.g * 0.45f, inkBase.b * 0.45f, 1f));
            // §3.1 피크 어둠 20.7% 대 우리 9.0% — 먹 몸통을 조금 되살린다(0.28 → 0.34).
            m.SetFloat("_InkBlend", 0.34f);
            // 🔑 R1 정정(§4.1, Q43-가): 「중간톤 제거」를 극단까지 민 것이 레퍼런스와 어긋난다 —
            // 실제 애니풍 레퍼런스의 중간톤은 18~37%다(경계 밴드에). 0.35 → 0.45로 바깥 계단의
            // 반투명을 한 단 되살린다. 탁함의 원인이던 「램프」가 아니라 「계단」으로 돌아오는 것이
            // 요점이다(계단화는 그대로, 경계 폭만 넓어진다).
            m.SetFloat("_OpaqueFrom", 0.45f);

            // Q4-나 · 네온은 먹 형태 안쪽 코어에서만.
            //
            // 🔴🔴 <b>Q22-가 — 코어를 블룸 문턱 위로 올린다.</b> 출하 전투 씬은
            // <c>ArtLookdevVolumeProfile</c>을 쓰고 그 <b>블룸 문턱이 0.75</b>인데, 옛 값
            // (가족색 × 1.5 · 부스트 1)의 휘도가 <b>0.66</b>이라 <b>0.09 차이로 블룸이 안 걸렸다</b> —
            // 「네온 코어」라고 이름 붙여 놓고 실제로는 아무것도 빛나지 않았다는 뜻이다.
            //
            // 코어를 흰쪽으로 조금 당기는 이유: 순수 가족색을 밝히기만 하면 <b>붉은 채로 밝아져</b>
            // 벽돌색이 된다. 중심이 살짝 탈색돼야 「달궈진」 것으로 읽힌다(팩의 화염 램프도
            // 흰색에서 출발한다 — 아래 <see cref="ApplyLifetimeRamp"/> 참조).
            //
            // ⚠️ 밝기를 <b>알파가 아니라 HDR 값</b>으로 버는 것이 요점이다. Q20-다로 겹 불투명도는
            // 그대로 두기로 했으므로, 12.5%로 찍히는 발광 림은 이 길로 블룸에 닿지 못한다
            // (문턱을 넘으려면 HDR이 6을 넘어야 한다). <b>코어만 100% 불투명도라 통과한다.</b>
            m.SetFloat("_CoreWidth", 0.10f);
            var coreHot = Color.Lerp(family, Color.white, 0.30f);
            m.SetColor("_CoreColor", new Color(coreHot.r * 1.5f, coreHot.g * 1.5f, coreHot.b * 1.5f, 1f));
            m.SetFloat("_CoreBoost", 1.5f);

            // Q2-나 · 면제 시스템은 폭 0으로 끈다(=키라인 없음).
            m.SetFloat("_EdgeWidth", keyline ? 0.13f : 0f);
            m.SetColor("_EdgeColor", Keyline);
            m.SetFloat("_EdgeBoost", 1f);

            // Q3-가 · 부드러운 방사 글로우 대신 형태를 두르는 얇은 발광 림.
            m.SetFloat("_RimWidth", 0.09f);
            m.SetColor("_RimColor", family);
            m.SetFloat("_RimBoost", 2.4f);

            // 🔑 R3 · boiling (Q38-ⓐ + Q45-가 확정, R1 실측 반영) — 실루엣이 계단 시간으로 끓는다.
            //
            // 주기 15fps = 레퍼런스 실측 중앙값(8.6~20fps 분포, Q45에서 「하나로 통일」 확정).
            // 진폭 0.055는 실측 변형강도 0.20~0.24(1−IoU)를 우리 실제 마스크(brushslash·shard)에
            // 오프라인 재현해 얻은 환산값이다 — 변형강도는 UV 진폭과 단위가 달라 직접 못 쓴다.
            // 타격 태그는 EnsureSpecMaterial에서 0.07(강도 0.32)로 덮어쓴다(기준표 §3.2).
            m.SetFloat("_BoilFps", 15f);
            m.SetFloat("_BoilStrength", 0.055f);
            m.SetFloat("_BoilScale", 6f);

            // 4겹은 알파 블렌딩에서만 성립한다 — 가산이면 어두운 겹(키라인·먹)이 아예 안 그려진다.
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        /// <summary>
        /// 프리팹 하나를 확정 사양으로 재입힌다.
        ///
        /// <para>🔴 <b>정점 색을 흰색으로 눕히는 것이 이 절차의 일부다.</b> 팩 원본은 startColor와
        /// colorOverLifetime에 자기 색을 박아 두는데, 확정 사양은 색을 전부 머티리얼(림·코어·먹)에서
        /// 내므로 정점 색이 남아 있으면 그 위에 <b>한 번 더 곱해져</b> 가족색이 어긋난다
        /// (파랑×노랑=초록 사고와 같은 꼴). <c>Tint(ps, white)</c>가 색 키만 눕히고 알파 키(=페이드
        /// 곡선)는 보존한다.</para>
        /// </summary>
        private static void Reskin(GameObject root, string tag, Color family, SkinRule rule)
        {
            // R1 기준표의 카테고리별 값(§3.2 타격 · §3.3 충격파)을 태그로 가른다.
            var impactTag = tag == "HeavyPunch" || tag == "ArtilleryTile" || tag == "HornCharge";
            var shockTag = tag == "RoarRipple" || tag == "FissureTile" || tag == "FissureRingTile"
                || tag == "PressureCross";

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null)
                {
                    continue; // 앞선 Drop이 부모째 지운 경우.
                }

                var skin = rule(ps.name);
                if (skin.Skip)
                {
                    continue;
                }

                if (skin.Drop)
                {
                    Object.DestroyImmediate(ps.gameObject);
                    continue;
                }

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null)
                {
                    continue;
                }

                Tint(ps, Color.white);
                ApplyLifetimeRamp(ps, family,
                    whiteEnd: impactTag ? 0.10f : 0.16f,
                    deepMul: shockTag ? 0.25f : 0.35f);

                Texture texture;
                float lumaAlpha;
                string slug;
                if (skin.Mask.HasValue)
                {
                    var mask = VfxInkMaskBaker.Load(skin.Mask.Value);
                    if (mask == null)
                    {
                        Debug.LogError("[MonsterAttackVfxBuilder] 형상 마스크가 없다 — 먼저 구울 것: "
                            + VfxInkMaskBaker.AssetPath(skin.Mask.Value));
                        continue;
                    }

                    texture = mask;
                    // 우리 마스크는 알파가 진짜 거리장이라 휘도에서 알파를 만들 필요가 없다.
                    lumaAlpha = 0f;
                    slug = skin.Mask.Value.ToString();

                    // 🏁 P2(Q49-가) — 강펀치는 파편·속도선을 «함께» 생성 그림으로 간다.
                    // 🔴 Shard만 갈면 화면의 0.3%만 바뀐다(벤치 검산 — 화면 지배는 Streak).
                    // 다른 큐의 절차 마스크는 P3 매핑표 확정 전까지 그대로 두므로 태그로 가른다.
                    if (tag == "HeavyPunch")
                    {
                        var gen = skin.Mask.Value == VfxInkMaskBaker.InkMaskKind.Shard ? GenSplatDf
                            : skin.Mask.Value == VfxInkMaskBaker.InkMaskKind.Streak ? GenStrokeDf
                            : null;
                        if (gen != null)
                        {
                            var genTex = AssetDatabase.LoadAssetAtPath<Texture>(gen);
                            if (genTex == null)
                            {
                                Debug.LogError("[MonsterAttackVfxBuilder] 생성 거리장이 없다 — "
                                    + "먼저 구울 것(tools/vfx-ink-bake/bake.py all): " + gen);
                                continue;
                            }

                            texture = genTex;
                            slug = gen == GenSplatDf ? "SplatGen" : "StreakGen";
                        }
                    }

                    if (skin.Arc.HasValue)
                    {
                        // 🔴🔴 <b>궤적을 되살리는 자리.</b> C단계는 여기서 팩 메시를 평면 쿼드로 갈아
                        // 끼웠고(아래 else 분기), 그 대가로 <b>휘두름을 통째로 잃었다</b> —
                        // 프레임으로 재면 여덟 장이 전부 같은 그림이었다(§14). 아크 리본은 궤적 모양을
                        // 스스로 들고, UV의 U축이 호의 진행 방향이라 훑기가 걸린다.
                        var arcMesh = VfxInkArcMeshBaker.Load(skin.Arc.Value);
                        if (arcMesh == null)
                        {
                            Debug.LogError("[MonsterAttackVfxBuilder] 아크 메시가 없다 — 먼저 구울 것: "
                                + VfxInkArcMeshBaker.AssetPath(skin.Arc.Value));
                            continue;
                        }

                        renderer.renderMode = ParticleSystemRenderMode.Mesh;
                        renderer.mesh = arcMesh;
                        renderer.alignment = ParticleSystemRenderSpace.Local;
                        EnableSweepStreams(ps, renderer);

                        // 🏁 스타일 축 Q58 — 아크의 형상 텍스처를 <b>ⓒ 셀 카툰 스우시</b>로
                        // 바꾼다(각진 스파이크 실루엣, 닫기 3px — 노치가 스타일). 3스타일 비교
                        // 영상 판정 "완성도는 ⓒ가 가장 높다"의 그 그림이다(벤치 c_cel 열).
                        var strokeDf = AssetDatabase.LoadAssetAtPath<Texture>(GenStrokeCelDf);
                        if (strokeDf == null)
                        {
                            Debug.LogError("[MonsterAttackVfxBuilder] ⓒ 획 거리장이 없다 — "
                                + "먼저 구울 것(tools/vfx-ink-bake/bake.py): " + GenStrokeCelDf);
                            continue;
                        }

                        texture = strokeDf;
                        slug = "StrokeCel";
                    }
                    else if (renderer.renderMode == ParticleSystemRenderMode.Mesh)
                    {
                        // 팩 메시(불꽃 구름·스파이크 다발)는 자기 형상을 이미 들고 있어서, 그 위에 우리
                        // 마스크를 얹으면 형상이 두 번 겹친다. 마스크가 형상이 되려면 판이 평평해야 한다.
                        renderer.mesh = BuiltinQuad();
                        renderer.alignment = ParticleSystemRenderSpace.Local;
                    }
                }
                else
                {
                    texture = ResolveMainTexture(renderer.sharedMaterial);
                    // 🔴 팩 텍스처는 가산 전제라 알파가 쓸모없다 — 1로 켜지 않으면 알파 블렌딩에서
                    // 쿼드 전체가 불투명한 검은 판이 된다(실측).
                    lumaAlpha = 1f;
                    slug = "Soft";
                }

                renderer.sharedMaterial = EnsureSpecMaterial(
                    "Ink_" + tag + "_" + slug + (skin.Keyline ? "_K" : string.Empty)
                        + (skin.Arc.HasValue ? "_Sweep" : string.Empty),
                    texture, family, skin.Keyline, lumaAlpha,
                    exempt: !skin.Mask.HasValue, sweep: skin.Arc.HasValue, tag: tag);
            }
        }

        /// <summary>확정 사양 머티리얼 에셋. 같은 (프리팹 · 형상 · 키라인 · 훑기) 조합은 한 장을 공유한다.</summary>
        private static Material EnsureSpecMaterial(
            string materialName, Texture texture, Color family, bool keyline, float lumaAlpha, bool exempt,
            bool sweep = false, string tag = null)
        {
            EnsureFolder(MaterialFolder);
            var path = MaterialFolder + "/" + materialName + ".mat";
            var shader = Shader.Find(InkShaderName);
            if (shader == null)
            {
                Debug.LogError("[MonsterAttackVfxBuilder] 먹 파티클 셰이더를 못 찾았다: " + InkShaderName);
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_MainTex", texture);
            // 면제 시스템은 색까지 눌러 둔다 — 공기가 가족색을 정면으로 주장하면 형태를 든
            // 시스템과 대비가 서지 않는다.
            material.SetColor("_Color", exempt ? family * 0.55f : family);
            material.SetFloat("_LumaAlpha", lumaAlpha);
            // 그레인 텍스처는 절차 노이즈로 갈 예정이라 비워 둔다 — 흰색이면 곱해도 무해하다.
            material.SetFloat("_InkStrength", 0f);

            if (exempt)
            {
                ApplyExemptWash(material, family);
            }
            else
            {
                ApplyConfirmedSpec(material, family, keyline);

                // 🔴 태그별 덮어쓰기는 반드시 <see cref="ApplyConfirmedSpec"/> 「뒤」다 —
                // 「기본값 → 개별 튜닝」 순서가 뒤집히면 조용히 무효가 된다(§16의 흰 줄 버그).
                if (tag == "HeavyPunch" || tag == "ArtilleryTile" || tag == "HornCharge")
                {
                    // 기준표 §3.2 — 타격은 가장 세게 끓는다(변형강도 0.32 ≈ shard 마스크 진폭 0.07).
                    material.SetFloat("_BoilStrength", 0.07f);
                }

                if (tag == "RoarRipple" || tag == "FissureTile" || tag == "FissureRingTile"
                    || tag == "PressureCross")
                {
                    // 기준표 §3.3 — 충격파는 계단을 6단으로(피크 중간톤 0.2% → 목표 34.6%의 손잡이).
                    // ⚠️4단은 채도가 0.58로 무너져 기각된 전례가 있다(§16) — 6단이 하한이다.
                    material.SetFloat("_Posterize", 6f);
                }
            }

            // 🏁 <b>배관은 맞았다</b> — 진행도는 <c>Custom1X → TEXCOORD0.z</c>로 온다.
            // 채널은 추측이 아니라 <c>ParticleSystemRenderer</c>가 표시하는 라벨에서 읽었고,
            // <c>_SweepDebug=2</c>(생 채널 덤프)로 확인했다.
            //
            // 🔴🔴 <b>이 블록은 반드시 <c>ApplyConfirmedSpec</c> 「뒤」에 와야 한다.</b>
            // 아크 전용 튜닝은 확정 사양을 <b>덮어쓰는</b> 것이 목적인데, 앞에 두면 확정 사양이
            // 되받아 덮는다. 실제로 §14.6에서 «플라스틱 하이라이트»를 없애려고 정한
            // <c>_CoreWidth 0.07</c>이 <c>ApplyConfirmedSpec</c>의 <c>0.10</c>에 덮여
            // <b>한 번도 적용된 적이 없었다</b>(출하 머티리얼 실측 0.10 — 흰 심이 획 두께의 51%).
            material.SetFloat("_SweepEnable", sweep ? 1f : 0f);
            if (sweep)
            {
                // 🔴🔴 <b>훑기 구간은 「수명 전체」를 쓰고, 대신 「수명」을 짧게 잡는다.</b>
                //
                // 두 번 헤맨 자리다. 처음엔 합을 1로 두고 수명을 1.0s로 뒀더니 지우기가
                // <b>보이는 창(첫 0.5초) 밖</b>에서 끝나 꼬리가 아예 안 나왔다. 그래서 합을 0.48로
                // 줄였더니 이번엔 <b>수명의 절반이 안 보이는 채로 살아</b> 큐 길이가 실제 연출의
                // 두 배로 잡혔다(연출 길이 데이터가 수명을 잰다).
                //
                // 🔑 <b>둘 다 푸는 답은 하나다 — 합 = 1로 두고 수명을 연출 길이와 같게 만든다.</b>
                // 그러면 「보이는 길이 = 파티클 수명 = 큐 길이」가 <b>정의상</b> 성립한다.
                //
                // 🏁 R1 확정(Q42-가, 기준표 §3.1): 레퍼런스 참격은 어택 0.275s · 홀드 0.075s ·
                // 릴리스 0.167s(총 0.52s)다 — 「획이 그려지는 시간」이 우리(0.09s)의 3배였다.
                // 수명을 0.50s로 늘리고(BuildClawSlash의 SetTiming) 비율을 실측대로 잡는다.
                material.SetFloat("_SweepIn", 0.53f);   // 0.00~0.53 들어온다 (0.265s)
                material.SetFloat("_SweepHold", 0.14f); // 0.53~0.67 꽉 찬 채 머문다 (0.07s)
                material.SetFloat("_SweepOut", 0.33f);  // 0.67~1.00 꼬리가 지워진다 (0.165s)
                material.SetFloat("_SweepSoft", 0.16f);

                // 스타일 축 Q58(ⓒ 셀 카툰) — 3스타일 비교 영상의 c_cel 열 손잡이 그대로:
                // 그레인 0(평면 셀 — 표면 결 없음) · 계단 8→5단(평면 톤, 🔴4단은 키라인이
                // 채움이 되는 금지선 위) · boiling 0.07(영상 판정 당시 값 — 셀도 실루엣은
                // 끓는다, Q38). BoilFps 등 시간 문법은 무변경. 출하가 판정 재료(벤치)와
                // 갈라지면 안 된다 — 확정 사양 뒤에 와야 유효(§16 교훈)한 것도 동일.
                material.SetFloat("_BoilStrength", 0.07f);
                material.SetFloat("_InkBlend", 0f);
                material.SetFloat("_Posterize", 5f);

                // 🔑 <b>광택 빼기.</b> 리본 단면 마스크는 완벽히 매끄러운 거리장이라, 그대로 두면
                // 한가운데 네온 코어가 <b>플라스틱 하이라이트</b>처럼 균일하게 뻗는다(사용자 판정
                // "붓질보다 칼날" · 이후 "중앙에 흰 줄"). 코어를 좁히고 흐르는 노이즈로 몸통을
                // 부수면 먹으로 돌아온다 — 팩이 표면 정보량을 버는 것과 같은 장치다.
                // 🔴🔴 <b>판정 Q28-가 「선명함 안C」.</b> «탁하다»의 원인은 채도가 아니라
                // <c>_OpaqueFrom = 1</c>이었다 — 출력 불투명도가 겹 판정값(<c>banded</c>)에 묶여
                // <b>바깥 겹일수록 반투명</b>하게 찍히고, 어두운 배경과 섞이며 <b>중간 밝기 램프</b>를 만든다.
                // 실측: 우리 채도는 팩보다 <b>높은데</b>(0.80 대 0.04~0.26) 「밝으면서 채도도 살아 있는
                // 빨강」이 <b>1.1%</b>뿐이었다. 몸통 색만 어둡게 한 안은 그 값이 <b>0.0%</b>였다 —
                // <b>색으로는 못 고친다</b>는 증거다.
                //
                // ⚠️ 계단을 4단까지 내리면 중간톤은 더 잡히지만 <b>채도가 0.58로 떨어진다</b> —
                // 밝은 면이 빨강을 잃고 흰색으로 날아간다. 「선명」이 아니라 「색이 없어짐」이라 기각했다.
                //
                // ⚠️ Q31-가에 따라 <b>지금은 참격(아크)에만</b> 건다. 나머지 29개 머티리얼은
                // 참격이 확정된 뒤 같은 축으로 훑는다.
                // 선명함(안C)은 이제 <c>ApplyConfirmedSpec</c>이 전 큐에 건다(Q31-가).
                // 여기서는 아크만의 차이인 <b>좁은 코어</b>만 덮어쓴다 — 리본이 얇아서
                // 확정폭 0.10이면 흰 심이 획을 반쯤 먹는다(실측 51%).
                material.SetFloat("_CoreWidth", 0.08f);
                // 스타일 축 Q57-가 — 그레인도 ⓑ 원화의 내부 결로 바꾼다(비백 결 → 조용한 농담).
                // ⓑ는 몸통이 균질해 그레인이 거의 평탄하다 — 위의 _InkBlend 0.22와 짝으로
                // 「결은 포인트만」이 성립한다. 강도·흐름 속도는 무변경.
                material.SetTexture("_InkTex", AssetDatabase.LoadAssetAtPath<Texture>(GenStrokeBoldGrain));
                material.SetFloat("_InkStrength", 0.38f);
                material.SetVector("_InkScroll", new Vector4(0.32f, 0.05f, 0f, 0f));

                // 🏁 Q65 확정(2026-08-09, 갤러리 §15 매트릭스 b_gray) — "너무 쨍하다"(Q59-나)의 답.
                // 몸통: 가족색 → 중간 검붉음 #B33A35(아크 전용 — family 자체를 바꾸면 림·키라인·
                // 다른 큐까지 움직인다) · 림: #B03A35·부스트 2.4→2.0(몸통에 연동해 한 단 가라앉힘) ·
                // 코어: 흰쪽 30%·부스트 1.5 → 회색 #9E9491·부스트 1 = 휘도가 블룸 문턱(0.75)
                // «아래»라 심은 남되 빛나지 않는다(의도된 무광 — 매트릭스에서 흰/회색/검정 3단을
                // 보고 회색을 골랐다). 🔴 확정 사양 «뒤» 블록이라 유효하다(§16 순서 교훈).
                material.SetColor("_Color", new Color(0.702f, 0.227f, 0.208f, 1f));
                material.SetColor("_RimColor", new Color(0.690f, 0.227f, 0.208f, 1f));
                material.SetFloat("_RimBoost", 2.0f);
                material.SetColor("_CoreColor", new Color(0.62f, 0.58f, 0.57f, 1f));
                material.SetFloat("_CoreBoost", 1.0f);
            }

            EditorUtility.SetDirty(material);
            ProducedMaterials.Add(path);
            return material;
        }

        /// <summary>재입힘으로 쓰이지 않게 된 옛 머티리얼을 지운다 — 남겨 두면 어느 것이 확정값인지
        /// 알 수 없어진다(갈래 1에서 실제로 겪은 문제다).</summary>
        private static int PruneOrphanMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                return 0;
            }

            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ProducedMaterials.Contains(path) && AssetDatabase.DeleteAsset(path))
                {
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>유니티 내장 쿼드. 마스크가 형상을 들려면 판은 평평하고 UV가 0~1이어야 한다.</summary>
        /// <summary>
        /// 셰이더가 <b>파티클 나이</b>를 읽을 수 있게 버텍스 스트림을 실어 보낸다.
        ///
        /// <para>🔴 기본 스트림에는 나이가 없다 — 이걸 부르지 않으면 <c>TEXCOORD0.z</c>가 의미 없는
        /// 값이고, 훑기가 <b>아무 데서나 잘린 그림</b>으로 나온다. 그래서 <c>_SweepEnable</c>과
        /// 이 호출은 <b>반드시 같이 다녀야 한다.</b></para>
        ///
        /// <para>🔑 <b>순서가 곧 채널 배치이고, 그 배치는 추측할 필요가 없다</b> —
        /// <c>ParticleSystemRenderer</c> 인스펙터의 Vertex Streams 목록이 스트림마다 실제 채널을
        /// 써 준다. 아래 순서에 대해 Unity가 내놓는 배치는
        /// POSITION.xyz / NORMAL.xyz / COLOR.xyzw / <b>TEXCOORD0.xy</b>(메시 UV) /
        /// <b>TEXCOORD0.z</b>(Custom1X)다. 셰이더의 <c>float4 uv : TEXCOORD0</c> 선언이 이 배치를
        /// 전제하며, <c>.w</c>는 <b>실려 오지 않는 자리라 항상 1.0</b>이다.</para>
        /// </summary>
        private static void EnableSweepStreams(ParticleSystem ps, ParticleSystemRenderer renderer)
        {
            // 진행도는 <b>Custom Data로 명시적으로 실어 보낸다.</b> 수명에 걸친 0→1 곡선을 우리가
            // 저작하므로 값의 의미가 렌더 모드에 안 휘둘리고, 채널도 목록 순서로 못박힌다.
            //
            // ⚠️ <c>AgePercent</c>로 바꿔도 같은 자리(<c>TEXCOORD0.z</c>)에 오지만 Custom Data를 유지한다 —
            // 곡선을 저작할 수 있어 나중에 리듬을 손볼 여지가 남는다.
            var custom = ps.customData;
            custom.enabled = true;
            custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            custom.SetVector(
                ParticleSystemCustomData.Custom1,
                0,
                new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)));

            var streams = new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.Custom1X,
            };
            renderer.SetActiveVertexStreams(streams);
        }

        private static Mesh BuiltinQuad()
        {
            return Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        }

        // ── A001 구조 이식(§17 ④) ────────────────────────────────────────────────────
        //
        // 🔑 <b>A001(`CFXR Hit D 3D (Yellow)`)이 충격파 기준선인 이유는 페이드가 아니라 시간 축이다.</b>
        // 실측(2026-08-08)한 세 시스템은 이렇게 어긋나 있다:
        //
        //   Ring          delay 0     · SoL 0.1 → 1     (10배로 자란다)  · Mesh
        //   Long spikes   burst 0.05  · SoL 0 → 1@0.5    · Stretch <b>lengthScale 10</b>
        //   중심 버스트    delay 0.05 + burst 0.05 = <b>0.10</b> · SoL 0.75 → 1.5 · Stretch <b>lengthScale 4</b>
        //
        // ⚠️ <b>인계문 §14.3의 "lengthScale 2 / 4 / 10"은 절반이 오독이다</b> — `2`는 Ring 렌더러의
        // 값이지만 Ring은 <b>Mesh 모드</b>라 lengthScale을 아예 읽지 않는다(Unity 기본값이 그냥 남아
        // 있는 것). 실제 편차는 <b>Stretch 두 벌의 4 : 10</b>이다. 그래서 편차를 만들려면 값을 하나
        // 바꾸는 게 아니라 <b>같은 획을 길이만 다른 형제로 갈라야</b> 한다 —
        // lengthScale은 파티클이 아니라 <b>렌더러</b> 단위라 한 시스템 안에서는 편차가 성립하지 않는다.

        /// <summary>
        /// A001 구조 ① — <b>수명 동안 계속 커진다.</b> 크기 곡선이 이 연출의 본체다
        /// (기준선의 링은 0.1 → 1, 즉 <b>10배</b>로 자란다). 우리 큐는 대부분 0.3~0.6에서
        /// 출발해 1로 가는 정도라 "이미 다 커진 채로 나타났다"로 읽혔다.
        /// </summary>
        private static void SetGrowth(ParticleSystem ps, float from, float to)
        {
            if (ps == null)
            {
                return;
            }

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        /// <summary>
        /// A001 구조 ③ — <b>시작 지연 계단.</b> 우리 큐는 대부분 시작 지연이 0 하나뿐이라
        /// 전부 동시에 터진다(A014만 0/0.06/0.12 계단이 있었다).
        /// </summary>
        /// <remarks>⚠️ 루프 시스템에는 걸 수 없다 — 호출부는 <see cref="StopLooping"/> 뒤여야 한다.</remarks>
        private static void SetStartDelay(ParticleSystem ps, float seconds)
        {
            if (ps == null)
            {
                return;
            }

            var main = ps.main;
            main.startDelay = seconds;
            // 지연을 준 만큼 duration이 짧으면 시스템이 방출 전에 끝나 <b>아무것도 안 나온다</b>.
            main.duration = Mathf.Max(main.duration, seconds + 0.12f);
        }

        /// <summary>
        /// A001 구조 ② — <b>같은 획을 길이만 다른 형제로 가른다</b>(기준선의 `Long spikes`가 정확히 이것이다).
        ///
        /// <para>🔴 <c>Instantiate</c>는 <b>자식을 통째로 데려온다.</b> A006의 획 시스템은 링·먹얼룩·균열선을
        /// 자식으로 달고 있어서, 지우지 않으면 프리팹 하나에 모듈이 두 벌씩 생긴다(= 밀도 2배).</para>
        ///
        /// <para>⚠️ <b>총 입자 수를 늘리지 말 것.</b> 원본 시스템의 수를 그만큼 덜어 내고 가른다 —
        /// 우리 획 마스크는 가닥마다 테두리를 가진 <b>그림</b>이라 수를 늘리면 성게가 된다(§규격 4 실측).</para>
        /// </summary>
        private static ParticleSystem AddStretchVariant(
            ParticleSystem source,
            string name,
            float lengthScale,
            int count,
            float startDelay,
            float lifetimeScale = 1f)
        {
            if (source == null || count <= 0)
            {
                return null;
            }

            // 🔴 <b>원본이 콘텐츠 루트면 형제로 두면 안 된다.</b> A006의 획 시스템(`CFXR2 Ground Hit`)은
            // 프리팹 <b>루트 그 자체</b>라 부모가 없고, 그 상태로 형제를 만들면 <see cref="Wrap"/>이
            // 루트만 옮기므로 변주가 씬에 남아 <b>저장에서 통째로 사라진다</b>(조용히).
            // 부모가 없으면 원본의 <b>자식</b>으로 붙인다 — 링·먹얼룩과 같은 자리다.
            var sibling = source.transform.parent != null;
            var go = Object.Instantiate(source.gameObject, sibling ? source.transform.parent : source.transform);
            go.name = name;
            go.transform.localPosition = sibling ? source.transform.localPosition : Vector3.zero;
            go.transform.localRotation = sibling ? source.transform.localRotation : Quaternion.identity;
            go.transform.localScale = sibling ? source.transform.localScale : Vector3.one;

            var children = new List<GameObject>();
            foreach (Transform child in go.transform)
            {
                children.Add(child.gameObject);
            }

            foreach (var child in children)
            {
                Object.DestroyImmediate(child);
            }

            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.startLifetime = ScaledCurve(main.startLifetime, lifetimeScale);
            SetStartDelay(ps, startDelay);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = lengthScale;
            return ps;
        }

        /// <summary>두 상수 범위를 보존한 채 배율을 먹인다 — <see cref="Scaled"/>는 최댓값만 남겨
        /// 범위를 뭉갠다(수명은 범위 자체가 편차의 원천이라 뭉개면 안 된다).</summary>
        private static ParticleSystem.MinMaxCurve ScaledCurve(ParticleSystem.MinMaxCurve curve, float factor)
        {
            if (curve.mode == ParticleSystemCurveMode.TwoConstants)
            {
                return new ParticleSystem.MinMaxCurve(curve.constantMin * factor, curve.constantMax * factor);
            }

            return new ParticleSystem.MinMaxCurve(curve.constant * factor);
        }

        /// <summary>
        /// 시스템이 <b>실제로 터지는 시각</b>을 한 숫자로 저작한다 = 시작 지연 + 버스트 시각.
        ///
        /// <para>🔴 <b>축이 둘이면 계단은 반드시 어긋난다.</b> 팩 프리팹은 버스트 시각에 0.05를
        /// 박아 두는데(A022의 폭발·불티·충격선 전부), 거기에 <c>startDelay</c>만 얹으면 저작한
        /// 숫자와 화면에 나오는 시각이 <b>말없이 갈라진다</b>. 버스트 시각을 0으로 눕히고
        /// 지연 하나만 남긴다 — 그래야 코드에 적힌 계단이 곧 화면의 계단이다.</para>
        /// </summary>
        private static void SetBeat(ParticleSystem ps, float seconds)
        {
            if (ps == null)
            {
                return;
            }

            var emission = ps.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            for (var i = 0; i < bursts.Length; i++)
            {
                bursts[i].time = 0f;
            }

            if (bursts.Length > 0)
            {
                emission.SetBursts(bursts);
            }

            SetStartDelay(ps, seconds);
        }

        /// <summary>버스트 하나짜리 시스템의 방출 수를 바꾼다(원본 버스트 시각은 보존한다).</summary>
        private static void SetBurstCount(ParticleSystem ps, int count)
        {
            if (ps == null)
            {
                return;
            }

            var emission = ps.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            if (bursts.Length == 0)
            {
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
                return;
            }

            bursts[0].count = count;
            emission.SetBursts(bursts);
        }

        // ── 헬퍼 ────────────────────────────────────────────────────────────────────

        /// <summary>원본 프리팹을 인스턴스화하고 <b>완전 언팩</b>한다. 언팩하지 않으면 저장 결과가
        /// 서드파티 프리팹의 변형(variant)이 되어, 팩을 갱신하면 우리 개조분이 같이 흔들린다.</summary>
        private static GameObject Clone(string assetPath)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                throw new System.InvalidOperationException("base VFX prefab not found: " + assetPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        private static GameObject Wrap(GameObject content, string rootName, Quaternion localRotation, float scale)
        {
            var root = new GameObject(rootName);
            Attach(content, root.transform, Vector3.zero, localRotation, scale);
            return root;
        }

        private static void Attach(GameObject content, Transform parent, Vector3 localPosition, Quaternion localRotation, float scale)
        {
            content.transform.SetParent(parent, false);
            content.transform.localPosition = localPosition;
            content.transform.localRotation = localRotation;
            content.transform.localScale = Vector3.one * scale;
            // 기본 scalingMode(Local)는 부모 스케일을 통째로 무시한다 — 래퍼 보정이 먹으려면 Hierarchy.
            foreach (var ps in content.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        private static string Save(GameObject root, string prefabName)
        {
            var path = Folder + "/" + prefabName + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }

        private static ParticleSystem Find(GameObject root, string systemName)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name == systemName)
                {
                    return ps;
                }
            }

            return null;
        }

        /// <summary>
        /// 🔴 틴트와 <b>colorOverLifetime 중화는 반드시 같이 다녀야 한다</b>. 최종 색은
        /// <c>startColor × colorOverLifetime</c>인데, 원본 팩들은 이 그라디언트에 자기 색을 박아 둔다
        /// — 실측으로 밟은 사고 둘: Hovl AoE는 키(0)이 <b>노랑</b>(1, 0.944, 0.373)이라 일렉트릭
        /// 블루를 곱하니 <b>초록</b>이 나왔고, CFXR Impact Glowing(Blue)는 파랑 그라디언트라 한글
        /// 레드가 <b>마젠타</b>가 됐다. 그래서 색 키만 흰색으로 눕히고 <b>알파 키는 보존</b>한다
        /// (알파는 페이드 곡선이라 건드리면 연출이 망가진다).
        /// </summary>
        private static void Tint(ParticleSystem ps, Color color)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);

            var overLifetime = ps.colorOverLifetime;
            if (!overLifetime.enabled)
            {
                return;
            }

            var source = overLifetime.color;
            var alphaKeys = source.mode == ParticleSystemGradientMode.Gradient && source.gradient != null
                ? source.gradient.alphaKeys
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

            var neutral = new Gradient();
            neutral.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys);
            overLifetime.color = new ParticleSystem.MinMaxGradient(neutral);
        }

        /// <summary>
        /// 🔴🔴 <b>Q21-가 — 수명 동안 「달아올랐다 식는」 밝기 램프.</b>
        ///
        /// <para>왜 필요한가: 재입힘 뒤 우리 파티클은 <b>40개 전부</b> 수명 동안 색이 변하지 않았다
        /// (같은 큐의 팩 원본은 21개가 변한다). <see cref="Tint"/>가 색 키를 흰색으로 눕히면서
        /// <c>colorOverLifetime</c>이 <b>알파 페이드만</b> 하는 모듈이 돼 버렸기 때문이다. 그래서
        /// 파티클이 한 색을 유지한 채 이동하다 사라졌고, 사용자 판정이 정확히 그것을 짚었다 —
        /// <b>"화려한 효과나 아무런 변화 없이 움직이는 것만으로 표현된다."</b></para>
        ///
        /// <para>🔑 <b>Q6(가족색 1색)과 충돌하지 않는다.</b> 색을 늘리는 게 아니라 <b>한 가족색의
        /// 밝기·채도가 시간 축에서 움직이는</b> 것이다. 곡선 모양은 팩에서 그대로 가져왔다 —
        /// CFXR <c>Explosion 1</c>의 램프가 <c>흰색 → 노랑 → 주황 → 심홍</c>으로,
        /// <b>흰색에서 출발해 G·B 채널을 점점 죽이며</b> 어두워지고 진해진다.</para>
        ///
        /// <para>🔴 <b>가족색을 정규화해서 쓰는 것이 핵심이다.</b> 이 그라디언트는 머티리얼 출력에
        /// <b>곱해지므로</b>, 가족색을 그대로 넣으면 가족색이 두 번 먹어 색이 어긋난다(파랑×노랑=초록
        /// 사고와 같은 꼴). 최대 채널을 1로 정규화하면 <b>같은 색상 안에서 깊어지기만</b> 하고
        /// 색상은 움직이지 않는다.</para>
        ///
        /// <para>⚠️ 알파 키는 <b>손대지 않는다</b> — 페이드 곡선이라 건드리면 연출 길이가 어긋난다.</para>
        /// </summary>
        private static void ApplyLifetimeRamp(
            ParticleSystem ps, Color family, float whiteEnd = 0.16f, float deepMul = 0.35f)
        {
            // R1 실측(기준표 §3.2·§3.3): 타격은 흰색 구간을 0.10으로 줄인다(섬광은 R4 플래시
            // 시스템이 별도로 맡는다) · 충격파는 끝 먹을 ×0.25로 더 짙게(「끝에서 가장 밝다」 결함 해소).
            var overLifetime = ps.colorOverLifetime;

            // 원래 알파 곡선을 살린다. 모듈이 꺼져 있었으면 "수명 내내 불투명"이 기본값이다.
            var source = overLifetime.color;
            var alphaKeys = overLifetime.enabled &&
                            source.mode == ParticleSystemGradientMode.Gradient &&
                            source.gradient != null
                ? source.gradient.alphaKeys
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };

            // 최대 채널을 1로 — 밝기만 실어 보내고 색상은 머티리얼이 낸 것을 그대로 통과시킨다.
            var peak = Mathf.Max(0.0001f, Mathf.Max(family.r, Mathf.Max(family.g, family.b)));
            var hue = new Color(family.r / peak, family.g / peak, family.b / peak, 1f);
            var deep = new Color(hue.r * deepMul, hue.g * deepMul, hue.b * deepMul, 1f);

            var ramp = new Gradient();
            ramp.SetKeys(
                new[]
                {
                    // 태어날 때는 흰색 = 머티리얼 색이 감쇠 없이 나온다(HDR 코어가 여기서 터진다).
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, whiteEnd),
                    // 중반에 가족색으로 내려앉는다.
                    new GradientColorKey(hue, 0.45f),
                    // 끝은 짙은 먹 — Q5-나의 "몸통은 짙은 먹"이 시간 축에서도 성립한다.
                    new GradientColorKey(deep, 1f),
                },
                alphaKeys);

            overLifetime.enabled = true;
            overLifetime.color = new ParticleSystem.MinMaxGradient(ramp);
        }

        /// <summary>
        /// 지면을 따라 한 방향으로 뻗는 네온 스트릭 한 가닥. <paramref name="yawDegrees"/>는
        /// 프리팹 전방(+Z) 기준이다 — 런타임이 +Z를 공격 방향에 얹으므로 이 각이 그대로
        /// 공격 기준 상대각이 된다.
        /// </summary>
        private static void AddStreakArm(
            GameObject parent,
            string name,
            float yawDegrees,
            float reach,
            Color color,
            Material material,
            float lifetime = 0.22f,
            float size = 0.5f,
            int count = 6,
            float delay = 0f)
        {
            if (material == null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = reach / lifetime;
            main.startSize = size;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3f;
            shape.radius = 0.12f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 5f;

            if (delay > 0f)
            {
                // 팩과 같은 함정을 피한다 — 버스트 시각이 아니라 startDelay 하나로 민다(SetBeat 계약).
                SetBeat(ps, delay);
            }
        }

        /// <summary>
        /// R4(Q44-가) — 타격 순간의 흰 섬광. 레퍼런스 임팩트류는 「밝고 무채색인」 픽셀이
        /// 0.28초 동안 이펙트의 30% 이상을 차지하는데(피크 40.1%) 우리 타격 큐는 1.6%였다
        /// (기준표 §3.5). Q39의 "1~2프레임"은 <b>켜짐(상승)</b>에만 적용되고, 지속은 실측대로
        /// 0.28s다(Q44-가) — 켜짐 1~2프레임 · 감쇠 0.23s.
        ///
        /// <para>🔴 <b>Reskin 「뒤」에 부를 것</b> — 규칙의 default가 이 시스템을 Soft(면제)로
        /// 재입혀 전용 머티리얼이 덮인다. 색 키는 흰색 고정이다(섬광의 정의가 「밝고 무채색」).</para>
        /// </summary>
        private static void AddImpactFlash(
            GameObject parent, string name, Vector3 localPosition, float size, float delay = 0f)
        {
            var material = EnsureFlashMaterial();
            if (material == null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = 0.28f;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var shape = ps.shape;
            shape.enabled = false;

            // 상승 1~2프레임(수명의 10% = 0.028s) + 지수 감쇠 — 기준표 §3.5의 「어택 0.23s」 근사.
            var over = ps.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.42f, 0.50f),
                    new GradientAlphaKey(0.12f, 0.80f),
                    new GradientAlphaKey(0f, 1f),
                });
            over.enabled = true;
            over.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;

            if (delay > 0f)
            {
                SetBeat(ps, delay);
            }
        }

        /// <summary>플래시 전용 머티리얼 — 4겹 사양이 아니다(섬광엔 실루엣도 키라인도 없다).
        /// <see cref="PruneOrphanMaterials"/>에 걸리지 않도록 ProducedMaterials에 등록한다.</summary>
        private static Material EnsureFlashMaterial()
        {
            EnsureFolder(MaterialFolder);
            var path = MaterialFolder + "/Ink_ImpactFlash.mat";
            var shader = Shader.Find(InkShaderName);
            if (shader == null)
            {
                Debug.LogError("[MonsterAttackVfxBuilder] 먹 파티클 셰이더를 못 찾았다: " + InkShaderName);
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            // 내장 소프트 원 — 팩 의존이 없다. 알파가 원을 들고 있어 _LumaAlpha가 필요 없다.
            material.SetTexture("_MainTex",
                AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            // 흰색 1.6 = 출하 블룸 문턱(0.75) 위 — 섬광이 블룸을 받아야 「빛」으로 읽힌다.
            material.SetColor("_Color", new Color(1.6f, 1.6f, 1.55f, 1f));
            material.SetFloat("_LumaAlpha", 0f);
            material.SetFloat("_InkBlend", 0f);
            material.SetFloat("_InkStrength", 0f);
            // 소프트 원을 3단 계단으로 — 부드러운 글로우가 아니라 만화의 섬광 링이 된다.
            material.SetFloat("_Posterize", 3f);
            material.SetFloat("_EdgeWidth", 0f);
            material.SetFloat("_RimWidth", 0f);
            material.SetFloat("_CoreWidth", 0f);
            material.SetFloat("_AlphaCut", 0.02f);
            material.SetFloat("_OpaqueFrom", 1f);
            // 섬광은 형상이 아니라 빛 — 끓이지 않는다.
            material.SetFloat("_BoilStrength", 0f);
            material.SetFloat("_SweepEnable", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            EditorUtility.SetDirty(material);
            ProducedMaterials.Add(path);
            return material;
        }

        private static Material LoadMaterial(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        }

        /// <summary>
        /// 팩마다 메인 텍스처 프로퍼티 이름이 다르다 — NAMU는 <c>Texture2D_3df04dd…</c> 같은
        /// 생성된 이름을 쓴다. <c>_MainTex</c>를 먼저 보고, 없으면 값이 들어 있는 첫 텍스처를 집는다.
        /// </summary>
        private static Texture ResolveMainTexture(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null)
            {
                return source.GetTexture("_MainTex");
            }

            var shader = source.shader;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                    continue;
                }

                var name = shader.GetPropertyName(i);
                var texture = source.GetTexture(name);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private static void SetTiming(GameObject root, string systemName, float duration, float lifetime)
        {
            var ps = Find(root, systemName);
            if (ps == null)
            {
                return;
            }

            var main = ps.main;
            main.duration = duration;
            main.startLifetime = lifetime;
        }

        private static void StopLooping(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
            }
        }

        /// <summary>모드 C 모듈에서 특히 중요하다 — 칸마다 인스턴스가 뜨므로 프리팹 하나에 붙은
        /// 실시간 광원이 18칸짜리 링에서 광원 18개가 된다.</summary>
        private static void StripLights(GameObject root)
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                Object.DestroyImmediate(light);
            }
        }

        /// <summary>
        /// 우리 형상 마스크 한 장을 <b>지면에 눕혀</b> 뿌리는 시스템. 균열선·파문 링처럼
        /// "바닥에 그려진 획"이 필요한 자리에 쓴다.
        ///
        /// <para>🔴 렌더 모드가 <b>Mesh + 내장 쿼드 + Local 정렬</b>이어야 한다. 빌보드로 두면
        /// 카메라를 향해 서 버려서 바닥에 그린 획이 아니라 <b>공중에 뜬 판</b>이 된다.</para>
        ///
        /// <para>머티리얼은 여기서 정하지 않는다 — 확정 사양 재입힘(<see cref="Reskin"/>)이
        /// 한 곳에서 입힌다. 색·겹이 두 군데서 정해지면 어느 쪽이 이겼는지 알 수 없어진다.</para>
        /// </summary>
        private static ParticleSystem AddGroundStroke(
            GameObject root,
            string name,
            int count,
            float size,
            float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 판을 바닥에 눕힌다.

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = Mathf.Max(0.25f, lifetime);
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); // 매번 같은 각이면 도장이 된다.

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = count > 1;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.35f;
            shape.radiusThickness = 1f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = BuiltinQuad();
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = null; // 형상 마스크와 확정 사양은 Reskin이 한 곳에서 입힌다.
            return ps;
        }

        /// <summary>먹 얼룩 레이어 — NamuFX 잉크 스플래시 머티리얼을 <b>참조만</b> 해서 얹는다.</summary>
        private static void AddInkBlot(GameObject root, string name, int count, float size, float lifetime, float speed)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(InkSplashMaterial);
            if (material == null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            // 색은 재입힘(Reskin)이 머티리얼에서 낸다 — 여기서 색을 박으면 그 위에 한 번 더 곱해진다.
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        /// <summary>포격이라는 게 읽히도록 위에서 내리꽂히는 낙하 스트릭을 얹는다 —
        /// 착탄만 있으면 "여기서 터졌다"이지 "떨어졌다"가 아니다.</summary>
        private static void AddFallStreak(GameObject content)
        {
            var donor = Find(content, "Lines");
            if (donor == null)
            {
                return;
            }

            var go = new GameObject("FallStreak");
            go.transform.SetParent(content.transform, false);
            // 원본 스케일이 0.32배로 눌리므로, 낙하 높이도 그 안에서 잡는다(로컬 6 ≈ 월드 1.9u).
            go.transform.localPosition = new Vector3(0f, 6f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = 0.18f;
            main.startSpeed = 34f;
            main.startSize = 0.6f;
            // 색은 재입힘(Reskin)이 머티리얼에서 낸다 — 여기서 색을 박으면 그 위에 한 번 더 곱해진다.
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(90f, 0f, 0f); // 아래로.

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = donor.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 6f;
        }

        private static ParticleSystem.MinMaxCurve Scaled(ParticleSystem.MinMaxCurve curve, float factor)
        {
            return new ParticleSystem.MinMaxCurve(curve.constantMax * factor);
        }

        private static Color Rgb(string hex, float alpha)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var color))
            {
                color = Color.white;
            }

            color.a = alpha;
            return color;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
