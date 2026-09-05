using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.EditorTools.Combat
{
    /// <summary>
    /// 참격의 <b>궤적 리본 메시</b>를 에셋으로 굽는다.
    ///
    /// <para>🔴🔴 <b>왜 평면 쿼드로는 안 되는가.</b> C단계는 팩의 아크 메시를 내장 Quad로 갈아
    /// 끼웠다("마스크가 형상이 되려면 판이 평평해야 한다", §10.2). 그 대가로 <b>궤적을 통째로
    /// 잃었다</b> — 프레임 스트립으로 재면 우리 참격은 <b>여덟 프레임이 전부 같은 그림</b>이고,
    /// 팩은 날이 들어와 호를 그리며 쓸고 꼬리를 남긴다(§14).</para>
    ///
    /// <para>🔑 <b>잔상의 정체는 Trail 모듈이 아니다</b>(팩도 안 쓴다). 휘두른 궤적 모양의
    /// <b>리본이 이미 거기 있고</b>, 디졸브가 그 리본을 <b>길이 방향으로 그려 나가는</b> 것이
    /// 잔상으로 읽힌다. 그래서 이 메시의 <b>UV·U축이 호의 진행 방향</b>이라는 점이 전부다 —
    /// 그 축이 없으면 셰이더가 "어디까지 그렸는지"를 말할 수 없다.</para>
    ///
    /// <para>⚠️ 리본은 <b>XY 평면에 눕고 +Z를 본다</b> — <b>내장 Quad와 같은 규약</b>이다.
    /// 이 메시는 Quad를 물고 있던 시스템에 그대로 갈아 끼우는 것이 목적이라, 평면이 다르면
    /// 기존 파티클 회전이 아크를 <b>모서리로 세워</b> 화면에서 사라진다. 루트에 보정 회전을 굽지
    /// 말 것(런타임 <c>Instantiate(prefab, pos, rot)</c>가 덮어쓴다).</para>
    /// </summary>
    public static class VfxInkArcMeshBaker
    {
        public const string MeshFolder = "Assets/Art/VFX/InkMeshes";

        /// <summary>호를 몇 토막으로 나눌 것인가. 이 수가 낮으면 <b>호가 각져</b> 붓질로 안 읽힌다.</summary>
        private const int Segments = 72;

        public enum ArcKind
        {
            /// <summary>발톱 참격 — 넓게 쓸고 지나가는 호. A003·A004·A013.</summary>
            Slash,

            /// <summary>좁고 긴 직선에 가까운 호 — 기합참·관통. A004 변주·A014.</summary>
            Thrust,

            /// <summary>거의 완전한 원 — 파문·충격파 링. A029·A006·A018.</summary>
            Ring,

            /// <summary>
            /// 발톱 3갈래 — <b>한 획이 아니라 세 줄이 동시에 긁힌 자국</b>이다.
            /// 가닥마다 반지름·각도·굵기를 어긋내 «짐승이 긁었다»로 읽히게 한다.
            /// </summary>
            ClawTriple,
        }

        public static string AssetPath(ArcKind kind)
        {
            return MeshFolder + "/ink_arc_" + kind.ToString().ToLowerInvariant() + ".asset";
        }

        public static Mesh Load(ArcKind kind)
        {
            return AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath(kind));
        }

        [MenuItem("Tools/Seoul Playup/Combat/Bake Ink VFX Arc Meshes")]
        public static void BakeAllMenu()
        {
            Debug.Log(BakeAll());
        }

        /// <summary>전량을 굽는다. 멱등 — 같은 경로의 에셋 내용을 갈아 끼운다.</summary>
        public static string BakeAll()
        {
            EnsureFolder(MeshFolder);
            var written = new System.Collections.Generic.List<string>();
            foreach (ArcKind kind in System.Enum.GetValues(typeof(ArcKind)))
            {
                written.Add(BakeToAsset(kind));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "[VfxInkArcMeshBaker] baked " + written.Count + " arc meshes:\n  " +
                   string.Join("\n  ", written);
        }

        public static string BakeToAsset(ArcKind kind)
        {
            EnsureFolder(MeshFolder);
            var path = AssetPath(kind);
            var baked = Build(kind);

            // 🔴 에셋을 <b>새로 만들어 덮으면 GUID가 바뀌어</b> 프리팹 참조가 끊긴다.
            // 이미 있으면 내용만 갈아 끼운다.
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                CopyInto(baked, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(baked);
            }
            else
            {
                AssetDatabase.CreateAsset(baked, path);
            }

            return path;
        }

        private static void CopyInto(Mesh from, Mesh to)
        {
            to.vertices = from.vertices;
            to.uv = from.uv;
            to.normals = from.normals;
            to.triangles = from.triangles;
            to.RecalculateBounds();
        }

        /// <summary>한 가닥의 저작 수치. 여러 가닥을 <b>한 메시에</b> 이어 붙여 긁은 자국을 만든다.</summary>
        private readonly struct Strand
        {
            public readonly float SpanDegrees;
            public readonly float Radius;
            public readonly float MaxHalf;
            public readonly float AngleOffsetDegrees;
            public readonly bool TaperBothEnds;

            public Strand(float spanDegrees, float radius, float maxHalf,
                          float angleOffsetDegrees = 0f, bool taperBothEnds = false)
            {
                SpanDegrees = spanDegrees;
                Radius = radius;
                MaxHalf = maxHalf;
                AngleOffsetDegrees = angleOffsetDegrees;
                TaperBothEnds = taperBothEnds;
            }
        }

        /// <summary>종류별 가닥 구성. 대부분 한 가닥이고 <see cref="ArcKind.ClawTriple"/>만 셋이다.</summary>
        private static Strand[] Strands(ArcKind kind, out float centerOffset)
        {
            switch (kind)
            {
                case ArcKind.Thrust:
                    // ⚠️ 호는 중심에서 <b>바깥으로만</b> 부풀므로, 원 중심을 그대로 원점에 두면
                    // 리본 전체가 한쪽으로 쏠려 화면 밖으로 잘린다(실측). 호의 apex와 양끝의
                    // 중간이 원점에 오도록 밀어 둔다 — 그래야 파티클 위치가 곧 연출의 한가운데다.
                    centerOffset = -1.43f;
                    return new[] { new Strand(66f, 1.55f, 0.13f, 0f, true) };

                case ArcKind.Ring:
                    // 🔑 완전한 원이 아니라 <b>한 곳이 끊긴</b> 원이다 — 등폭 완전원은 스티커로 읽힌다
                    // (마스크 쪽 RingDistance가 같은 이유로 여백을 판다).
                    centerOffset = 0f;
                    return new[] { new Strand(322f, 0.86f, 0.075f, 0f, true) };

                case ArcKind.ClawTriple:
                    // 🔑 <b>발톱은 한 획이 아니다.</b> 세 가닥의 반지름을 벌려 부챗살로 퍼지게 하고,
                    // 각도를 조금씩 어긋내 나란한 평행선으로 보이지 않게 한다(평행하면 «긁은 자국»이
                    // 아니라 «줄무늬»로 읽힌다). 가운데를 가장 길고 굵게 둬서 주된 획이 읽히게 한다.
                    //
                    // ⚠️ centerOffset은 <b>가운데 가닥</b> 기준이다(−0.703 × 반지름).
                    centerOffset = -0.69f;
                    return new[]
                    {
                        new Strand(120f, 0.86f, 0.072f, -5.5f),
                        new Strand(132f, 0.98f, 0.098f, 0f),
                        new Strand(116f, 1.10f, 0.066f, 4.5f),
                    };

                default:
                    // ⚠️ 두께는 <b>붓질이냐 칼날이냐</b>를 가른다. 0.17에서는 리본이 통통한 마름모라
                    // 광택 있는 칼날로 읽혔다(사용자 판정). 팩의 먹 참격은 훨씬 얇다.
                    centerOffset = -0.65f;
                    return new[] { new Strand(132f, 0.92f, 0.115f) };
            }
        }

        /// <summary>메모리 메시 한 장. 벤치도 이걸 부른다 — 벤치와 출하가 갈라지면 안 된다.</summary>
        public static Mesh Build(ArcKind kind)
        {
            var strands = Strands(kind, out var centerOffset);

            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            foreach (var strand in strands)
            {
                AppendStrand(strand, centerOffset, vertices, uv, normals, triangles);
            }

            var mesh = new Mesh { name = "InkArc_" + kind };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 가닥 하나를 리스트에 이어 붙인다.
        ///
        /// <para>🔴🔴 <b>가닥마다 UV.x가 0→1로 독립해서 간다.</b> 훑기는 U축을 «어디까지 그렸는지»로
        /// 읽으므로, 세 가닥을 한 메시에 이어 붙이면서 U를 전체 길이로 이어 버리면 <b>가닥이 차례로</b>
        /// 그려진다 — 발톱은 세 줄이 <b>동시에</b> 그어져야 한다.</para>
        /// </summary>
        private static void AppendStrand(Strand strand, float centerOffset,
            List<Vector3> vertices, List<Vector2> uv, List<Vector3> normals, List<int> triangles)
        {
            var baseIndex = vertices.Count;
            var half = strand.SpanDegrees * 0.5f * Mathf.Deg2Rad;
            var angleOffset = strand.AngleOffsetDegrees * Mathf.Deg2Rad;

            for (var i = 0; i <= Segments; i++)
            {
                var t = i / (float)Segments;
                var angle = Mathf.Lerp(-half, half, t) + angleOffset;

                // 🔴🔴 <b>XY 평면에 눕는다(+Z를 본다) — 내장 Quad와 같은 규약이다.</b>
                // 이 프리팹들은 원래 Quad를 물고 있었고 파티클 회전이 <b>그 규약에 맞춰 저작</b>돼 있다.
                // XZ 평면(+Y를 봄)으로 구우면 같은 회전이 걸린 순간 아크가 <b>모서리로 서서</b>
                // 화면에서 완전히 사라진다 — 훑기를 꺼도 안 보여서 한참 헤맸다(실측).
                var dir = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                var center = new Vector3(0f, centerOffset, 0f) + dir * strand.Radius;

                // 붓: 눌렀다 떼는 획 — 가운데가 부풀고 끝으로 맺힌다.
                //
                // 🔴 <c>Max(0, …)</c>가 <b>장식이 아니다.</b> 이 루프는 <c>t = 1</c>을 정확히 밟는데
                // <c>Sin(π)</c>는 부동소수 오차로 <b>−8.7e-8</b>이 나온다. 음수를 소수 거듭제곱하면
                // <c>NaN</c>이고, 정점 하나가 NaN이면 <c>RecalculateBounds</c>가 <b>−Infinity 바운드</b>를
                // 내놓아 Unity가 <b>메시를 통째로 컬링한다</b> — 화면에서 참격이 흔적 없이 사라진다(실측).
                var swell = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t))), 0.55f);
                var width = strand.MaxHalf * swell;
                if (!strand.TaperBothEnds)
                {
                    // 시작이 굵고 끝으로 갈수록 가늘어진다(휘두른 방향이 읽히게).
                    width *= Mathf.Lerp(1.15f, 0.4f, t);
                }

                var inner = center - dir * width;
                var outer = center + dir * width;

                vertices.Add(inner);
                vertices.Add(outer);

                // 🔑 <b>U가 호의 진행 방향</b>이다 — 디졸브가 이 축을 따라 그려 나간다.
                uv.Add(new Vector2(t, 0f));
                uv.Add(new Vector2(t, 1f));

                // Quad와 같은 방향을 본다. 셰이더가 언릿 + Cull Off라 조명엔 안 쓰이지만,
                // 다른 도구가 노멀을 보고 방향을 추론할 수 있으므로 규약을 맞춰 둔다.
                normals.Add(Vector3.back);
                normals.Add(Vector3.back);

                if (i < Segments)
                {
                    var a = baseIndex + i * 2;
                    var b = a + 1;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 2);
                    triangles.Add(b);
                    triangles.Add(b + 2);
                    triangles.Add(a + 2);
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
