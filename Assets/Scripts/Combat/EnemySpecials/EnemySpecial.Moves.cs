using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 필살기 11종. 무협의 기법에서 가져온 연출이며(경공 잔상, 천근추, 사자후, 점혈, 금종조, 격산타우 등) 다른 작품의 고유 기술을 옮긴 것은 아니다.
/// 좌우는 S 로 뒤집는다: S = +1 이면 적이 주인공의 오른쪽에 있다. "주인공에서 적 쪽으로 d" 는 HeroX + S * d 다.
/// 피해 수치는 막히지 않았을 때의 합이다(주인공 체력 100 기준). 동작은 지금 있는 애니메이션 상태에 몸 전체의 뛰기·돌기·기울이기를 얹은 것이다.
/// </summary>
public sealed partial class EnemySpecial
{
    // 01 산길 척후병 · 잔영연환각 (7 + 8 + 13 = 28): 경공으로 잔상 둘을 남긴다. 잔상이 먼저 차고 본체는 위에서 떨어진다.
    IEnumerator AfterimageKicks()
    {
        yield return Gather(.3f, .8f);
        float hx = HeroX, start = FoeX; Vector3 heroGround = Ground(hx);
        Pull(.8f, .3f);
        Vector3 near = Ground(hx + S * .95f) - Far * .5f, far = Ground(hx + S * .95f) + Far * .5f;
        Fx(qi.muzzle, new Vector3(start, 1f, 0), LookOut, .7f);
        Streak(new Vector3(start, 1.2f, 0), near + Vector3.up * 1.2f, .12f, .2f); Streak(new Vector3(start, .7f, 0), far + Vector3.up * .7f, .12f, .2f);
        Ghost(near, heroGround, "LOWKICK", .7f);
        ShowFoe(false); Place(gameObject, hx + S * 1f);
        yield return Hold(.2f); Strike(7, Away(.3f), .5f, .04f, .4f, hx, 1.5f);
        Ghost(far, heroGround, "HIGHKICK", .75f);
        yield return Hold(.3f); Strike(8, Away(.3f), .55f, .06f, 1.55f, hx, 1.5f);
        yield return Hold(.12f);
        // 본체는 위에서 떨어진다(지금 주인공이 있는 곳으로)
        ShowFoe(true); Place(gameObject, HeroX + S * .9f); foeLift.y = 2.3f; if (foeView != null) foeView.PlaySpecial("HIGHKICK");
        Streak(new Vector3(FoeX + S * .2f, 3.4f, 0), new Vector3(FoeX - S * .35f, 1.5f, 0), .16f, .22f);
        Tween(.18f, u => foeLift.y = Mathf.Lerp(2.3f, 0f, u * u));
        yield return Hold(.19f); Strike(13, Away(5.5f), 1f, .24f, 1.5f); Burst(Ground(FoeX), .5f);
        Shockwave(Ground(FoeX), .2f, 1.9f, .32f); Sparks(new Vector3(HeroX + S * .2f, 1.5f, 0), Out + Vector3.up * .4f, 26, 7f, .6f, 50f, 1.4f);
        yield return Hold(.4f);
        yield return Settle();
    }

    // 02 계곡 잠복병 · 잠룡출수 (26): 물안개 속으로 가라앉아 물결로 다가온 뒤 물기둥과 함께 솟구친다. 물결이 향한 자리에서 벗어나면 맞지 않는다.
    IEnumerator DragonRise()
    {
        yield return Gather(.45f, .8f, "GUARD_LOWKICK_HOLD");
        float hx = HeroX, start = FoeX;
        Smoke(new Vector3(start, .6f, 0), new Vector3(1.4f, 1f, 1.2f), 1.7f, 0, .3f, 1.4f, .3f, 26);
        Burst(new Vector3(start, .4f, 0), .7f);
        Tween(.24f, u => foeLift.y = Mathf.Lerp(0f, -2.3f, u * u), () => ShowFoe(false));
        yield return Hold(.32f);
        var wake = Wave(new Vector3(start, .14f, 0), LookOut, new Vector3(.25f, .25f, .7f));
        Move(wake.transform, new Vector3(hx + S * .75f, .14f, 0), .5f, () => EndWave(wake));
        for (int i = 0; i <= 5; i++)
        {
            float x = Mathf.Lerp(start, hx + S * .8f, i / 5f);
            Shockwave(new Vector3(x, 0f, 0), .15f, .95f, .55f, 1.7f);
            Smoke(new Vector3(x, .2f, 0), new Vector3(.5f, .1f, .5f), .7f, 0, .2f, .7f, .6f, 4);
            yield return Hold(.085f);
        }
        Place(gameObject, hx + S * .85f); Pull(1.4f, .3f);
        yield return Hold(.14f);
        float px = hx + S * .6f;
        var pillar = Wave(new Vector3(px, .2f, 0), Up, new Vector3(.9f, .9f, 2.6f));
        Move(pillar.transform, new Vector3(px, 2.7f, 0), .26f, () => EndWave(pillar), 2);
        Smoke(new Vector3(px, 1.3f, 0), new Vector3(.9f, 2.4f, .9f), 1.3f, 0, .3f, 1.1f, 2.4f, 30);
        for (int k = 0; k < 7; k++) Streak(new Vector3(px + Rand(-.4f, .4f), 0, Rand(-.3f, .3f)), new Vector3(px + Rand(-.7f, .7f), Rand(2.2f, 3.6f), Rand(-.3f, .3f)), .07f, .3f);
        Burst(Ground(px), .9f); Flash(new Vector3(px, 1.2f, 0) - Far, 6f, 6f, .3f);
        Sparks(new Vector3(px, .3f, 0), Vector3.up, 70, 10f, .95f, 24f, 2.4f, .07f);   // 물보라
        ShowFoe(true); if (foeView != null) foeView.PlaySpecial("CROSS");
        Tween(.12f, u => foeLift.y = Mathf.Lerp(-2.3f, 0f, u), () => Jump(true, .9f, .45f));
        yield return Hold(.15f);
        if (Strike(26, new Vector2(-S * 2.5f, 0f), 1.1f, .26f, 1.3f, px, 1.3f)) Launch(1.5f, .7f, -38f);
        yield return Hold(.65f);
        yield return Settle();
    }

    // 03 고용 산적 · 천근추지렬 (34): 높이 뛰어 온몸으로 내리찍으면 땅이 갈라지고 바위가 튄다. 맞히는 것은 주먹이 아니라 갈라지는 땅이다.
    IEnumerator ThousandWeight()
    {
        yield return Gather(.7f, 1.4f);
        float land = Mathf.Abs(FoeX - HeroX) > 1.8f ? HeroX + S * 1.8f : FoeX;   // 주인공 한 걸음 앞에 떨어진다
        if (foeView != null) foeView.PlaySpecial("SPECIAL_CHARGE"); Pull(2.4f, .35f);
        Slide(gameObject, land, .62f);
        Tween(.62f, u => foeTilt = Mathf.Lerp(-16f, 34f, u));
        bool landed = false; Jump(true, 1.9f, .62f, () => landed = true);
        while (!landed) yield return null;
        foeTilt = 0f; if (foeView != null) foeView.PlaySpecial("LOWKICK"); Shake(.34f, 4f);
        Burst(Ground(land - S * .2f), 1.5f); Flash(new Vector3(land, .8f, 0) - Far, 7f, 7f, .35f);
        Smoke(new Vector3(land, .35f, 0), new Vector3(2.6f, .4f, 1.6f), 1.9f, 0, .3f, 1.5f, .6f, 30);
        Ring(u => new Vector3(land, .05f, 0), Vector3.up, u => Mathf.Lerp(.3f, 4.6f, u), .4f, .08f, 2.6f); Shockwave(Ground(land), .3f, 4.8f, .45f, 2.4f);
        Sparks(new Vector3(land - S * .2f, .15f, 0), new Vector3(-S * .5f, 1f, 0), 60, 9f, .9f, 70f, 2f); Embers(new Vector3(land - S * 1f, .5f, 0), new Vector3(3.6f, .6f, 1.4f), 40, 1.6f, 1.6f);
        Cracks(Ground(land - S * .35f), -S, Mathf.Abs(land - HeroX) + 1.9f, 6, 30f, .22f);
        Debris(Ground(land - S * .5f), 7, 6.5f, .14f, .34f, -S);
        yield return Hold(.2f);
        if (Strike(34, Away(6.5f), 1.3f, .4f, .6f, land, 4.2f))
        {
            Debris(Ground(HeroX), 6, 7.5f, .16f, .4f);
            Smoke(new Vector3(HeroX, .4f, 0), new Vector3(1.4f, .5f, 1.2f), 1.5f, 0, .3f, 1.3f, .9f, 14);
            Launch(1.3f, .65f, -32f);
        }
        yield return Hold(.65f);
        yield return Settle();
    }

    // 04 옛 나루 약탈자 · 승표탈기 (8 + 16 = 24, 게이지 탈취): 밧줄 표창으로 감아 끌어당기고, 모아 둔 필살기 게이지를 비운다. 막거나 피하면 밧줄이 풀린다.
    IEnumerator RopeDart()
    {
        yield return Gather(.3f, .7f);
        if (foeView != null) foeView.PlaySpecial("JAB");
        yield return Hold(.1f);
        var dart = Wave(Hands(), LookOut, new Vector3(.22f, .22f, .45f));
        // 쇠 표창 머리(창날과 고리)와 꼬인 삼 밧줄
        var head = Lathe(Hands(), new[] { new Vector2(0f, 0f), new Vector2(.2f, .05f), new Vector2(.24f, .026f), new Vector2(.3f, .026f), new Vector2(.31f, .044f), new Vector2(.34f, .044f), new Vector2(.36f, 0f) }, IronMaterial(new Vector2(2f, 1f), Hdr(spec.tint, .3f)), 14);
        head.transform.rotation = Quaternion.Euler(0f, 0f, S > 0f ? -90f : 90f);
        var rope = Line(new Material(fx.rope), .05f, .05f, 4f); rope.textureMode = LineTextureMode.Tile; var glow = Line(Glow(1.6f), .016f, .016f, 4f);
        bool roped = true;
        updaters.Add(() =>
        {
            if (!roped || !rope || !dart) { if (rope) rope.positionCount = 0; if (glow) glow.positionCount = 0; if (head) Destroy(head); return true; }
            Vector3 a = Hands(), b = dart.transform.position; const int n = 16;
            head.transform.position = b + Out * .16f;
            rope.positionCount = glow.positionCount = n;
            for (int i = 0; i < n; i++) { float t = i / (n - 1f); var p = Vector3.Lerp(a, b, t) + Vector3.down * (.22f * Mathf.Sin(t * Mathf.PI)); rope.SetPosition(i, p); glow.SetPosition(i, p); }
            return false;
        });
        float aim = HeroX;
        Move(dart.transform, new Vector3(aim + S * .25f, 1.2f, 0), .14f);
        yield return Hold(.14f);
        bool caught = Strike(8, Vector2.zero, .5f, .1f, 1.15f, aim, 1.1f) && !guardedSpecial;
        if (!caught)
        {
            roped = false; EndWave(dart);
            yield return Hold(.3f);
            yield return Settle();
            yield break;
        }
        Stun(1.5f);
        // 허리에 감긴다
        float hx = HeroX;
        var wrapA = Ring(u => new Vector3(HeroX, 1.05f, 0), Vector3.up, u => .42f, 1.3f, .035f, 2.2f, false);
        var wrapB = Ring(u => new Vector3(HeroX, 1.3f, 0), Vector3.up, u => .40f, 1.3f, .035f, 2.2f, false);
        yield return Hold(.2f);
        // 끌어당긴다 (적 앞에서 멈춘다)
        if (foeView != null) foeView.PlaySpecial("GUARD_HOLD");
        float stop = Mathf.Abs(FoeX - hx) > 1.05f ? FoeX - S * 1.05f : hx;
        Slide(hero.gameObject, stop, .3f); Tween(.3f, u => { if (dart) dart.transform.position = new Vector3(Mathf.Lerp(hx, stop, u) + S * .25f, 1.2f, 0); heroTilt = 14f * Mathf.Sin(u * Mathf.PI); });
        yield return Hold(.32f);
        roped = false; EndWave(dart); if (wrapA) Destroy(wrapA.gameObject); if (wrapB) Destroy(wrapB.gameObject);
        // 기운을 빼앗는다: 주인공이 모아 둔 파운장 게이지가 비워진다
        Stun(1.1f);
        Drain(() => HeroChest, Hands, 14, .55f);
        yield return Hold(.3f);
        DrainTarget();
        Fx(qi.ready, Hands(), Quaternion.identity, 2.2f, 1.2f);
        yield return Hold(.4f);
        if (foeView != null) foeView.PlaySpecial("HIGHKICK");
        yield return Hold(.25f); Strike(16, Away(5f), 1.1f, .28f, 1.5f);
        yield return Hold(.3f);
        yield return Settle();
    }

    // 05 철문 · 금종폐관 (30): 금종조로 몸을 두르고(모으는 동안 맞아도 끊기지 않는다) 내려앉으면 쇠문 두 짝이 솟아 닫힌다. 문 사이에서 벗어나면 맞지 않는다.
    IEnumerator GoldenBellGate()
    {
        float bx = FoeX;
        // 금종조: 가장자리가 빛나는 기운 껍데기 위에 종 표면 무늬가 천천히 돈다
        var profile = new[] { new Vector2(0f, 1.24f), new Vector2(.12f, 1.15f), new Vector2(.6f, 1.12f), new Vector2(1.1f, 1.07f), new Vector2(1.55f, .97f), new Vector2(1.92f, .8f), new Vector2(2.2f, .56f), new Vector2(2.4f, .28f), new Vector2(2.5f, .1f), new Vector2(2.62f, .1f) };
        var shellMat = OrbMaterial(Hdr(spec.tint, .1f), Hdr(spec.tint, 1.5f), 2.2f); var bell = Lathe(Ground(bx), profile, shellMat, 56);
        var patternMat = RibbonMaterial(fx.bellTex, Hdr(spec.tint, .85f), 0f, .2f, 0f, Vector4.zero);
        var pattern = Lathe(Ground(bx), profile, patternMat, 56, 2f, 1f); pattern.transform.SetParent(bell.transform, false); pattern.transform.localPosition = Vector3.zero;
        Tween(2.6f, u =>
        {
            if (!bell) return;
            float open = Mathf.Lerp(.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 6f)));
            bell.transform.position = Ground(FoeX);
            bell.transform.localScale = new Vector3(open, Mathf.Lerp(.6f, 1f, open), open) * (1f + .012f * Mathf.Sin(u * 60f));
            bell.transform.rotation = Quaternion.Euler(0f, u * 60f, 0f);
            float glow = (.8f + .2f * Mathf.Sin(u * 45f)) * (1f - Mathf.Clamp01((u - .8f) / .2f));
            shellMat.SetFloat("_Alpha", glow); patternMat.SetFloat("_Intensity", glow);
        }, () => { if (bell) Destroy(bell); });
        foreach (var hoop in new[] { new Vector2(.03f, 1.24f), new Vector2(1.55f, .97f), new Vector2(2.2f, .56f), new Vector2(2.56f, .1f) })
            Ring(u => new Vector3(FoeX, hoop.x, 0), Vector3.up, u => hoop.y * Mathf.Lerp(.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 6f))), 2.5f, .035f, 1.8f);
        yield return Gather(.8f, 1.2f);
        bx = FoeX;
        // 육중한 몸이 짧게 떴다가 내려앉는다
        Pull(1.9f, .35f);
        if (foeView != null) foeView.PlaySpecial("SPECIAL_CHARGE"); bool landed = false; Jump(true, .9f, .44f, () => landed = true); Tween(.44f, u => foeTilt = Mathf.Lerp(-8f, 10f, u));
        while (!landed) yield return null;
        foeTilt = 0f; if (foeView != null) foeView.PlaySpecial("LOWKICK"); Shake(.28f, 4.5f); Burst(Ground(bx), 1.1f);
        Smoke(new Vector3(bx, .3f, 0), new Vector3(2.2f, .3f, 1.5f), 1.6f, 0, .3f, 1.3f, .5f, 22);
        Ring(u => new Vector3(bx, .05f, 0), Vector3.up, u => Mathf.Lerp(.3f, 3.8f, u), .36f, .07f, 2.6f); Shockwave(Ground(bx), .3f, 4f, .42f, 2.2f);
        Cracks(Ground(bx - S * .4f), -S, 2.2f, 3, 24f, .16f); Cracks(Ground(bx + S * .4f), S, 1.5f, 2, 22f, .16f);
        yield return Hold(.14f);
        float hx = HeroX;
        Seal("閉", new Vector3(hx, 2.0f, 0) + Far * 1.3f, 3.0f, 1.5f);
        // 쇠문 두 짝이 땅에서 솟는다 → 한 박자 → 닫힌다. 널빤지는 결이 서로 다른 오래된 나무, 띠·못·문고리는 망치 자국과 푸른 녹이 있는 청동이다.
        var band = BronzeMaterial(new Vector2(4f, .5f), Hdr(spec.tint, .05f)); var bronze = BronzeMaterial(new Vector2(1.5f, 1f), Hdr(spec.tint, .07f)); var rail = BronzeMaterial(new Vector2(.4f, 6f), Hdr(spec.tint, .05f));
        var leaves = new List<Transform>(); const float leafW = 1.5f, leafH = 3.3f, leafT = .3f, apart = 1.8f, shut = .96f; float face = -Far.z * leafT * .5f;
        foreach (int side in new[] { -1, 1 })
        {
            var leaf = NewObject("Gate leaf").transform; leaf.position = new Vector3(hx + side * apart, -1.85f, 0);
            for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Cube, new Vector3((i - 2) * leafW / 5f, 0, (i % 2 == 0 ? .5f : -.5f) * face * .1f), new Vector3(leafW / 5f + .004f, leafH, leafT), WoodMaterial(Rand(.8f, 1.08f)));
            foreach (float y in new[] { -.38f, 0f, .38f })
            {
                Part(leaf, PrimitiveType.Cube, new Vector3(0, y * leafH, 0), new Vector3(leafW + .04f, .17f, leafT + .06f), band);
                for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Sphere, new Vector3((i - 2) * leafW / 5f, y * leafH, face * 1.22f), new Vector3(.115f, .115f, .08f), bronze);
            }
            foreach (float y in new[] { -.19f, .19f }) for (int i = 0; i < 5; i++) Part(leaf, PrimitiveType.Sphere, new Vector3((i - 2) * leafW / 5f, y * leafH, face * 1.03f), new Vector3(.06f, .06f, .04f), bronze);
            foreach (int edge in new[] { -1, 1 }) Part(leaf, PrimitiveType.Cube, new Vector3(edge * leafW * .5f, 0, 0), new Vector3(.09f, leafH + .04f, leafT + .08f), rail);
            Vector3 knob = new Vector3(-side * leafW * .3f, .19f * leafH, face * 1.3f);
            Part(leaf, PrimitiveType.Sphere, knob, new Vector3(.27f, .27f, .12f), bronze);
            var handle = Torus(Vector3.zero, .12f, .022f, bronze, 28, 10, 3f).transform; handle.SetParent(leaf, false); handle.localPosition = knob + new Vector3(0f, -.11f, face * .5f); handle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Move(leaf, new Vector3(hx + side * apart, leafH * .5f, 0), .2f, null, 2); leaves.Add(leaf);
            Burst(Ground(hx + side * apart), .7f); Debris(Ground(hx + side * apart), 4, 5f, .1f, .24f);
            Smoke(new Vector3(hx + side * apart, .3f, 0), new Vector3(1.6f, .3f, 1f), 1.2f, 0, .3f, 1.1f, .6f, 12);
            Destroy(leaf.gameObject, 3.4f);
        }
        Flash(new Vector3(hx, 1.6f, 0) - Far * 1.5f, 5f, 9f, 1.2f);
        Shake(.2f, 5f);
        yield return Hold(.46f);
        if (foeView != null) foeView.PlaySpecial("SPECIAL_RELEASE");
        yield return Hold(.12f);
        foreach (var leaf in leaves) if (leaf) Move(leaf, new Vector3(hx + Mathf.Sign(leaf.position.x - hx) * shut, leafH * .5f, 0), .09f, null, 1);
        yield return Hold(.09f);
        if (Strike(30, Vector2.zero, 1.4f, .5f, 1.15f, hx, .95f)) Stun(.7f);
        Burst(new Vector3(hx, 2.4f, 0), .9f); Flash(new Vector3(hx, 1.4f, 0) - Far, 8f, 6f, .3f);
        for (int k = 0; k < 8; k++) Streak(new Vector3(hx, Rand(.3f, 3f), 0) - Far * .2f, new Vector3(hx + Rand(-1.4f, 1.4f), Rand(.2f, 3.4f), 0) - Far * Rand(.4f, 1.2f), .05f, .3f, 2.6f);
        for (int k = 0; k < 4; k++) Sparks(new Vector3(hx, .5f + k * .75f, 0) - Far * .25f, Vector3.up * .3f - Far, 22, 6.5f, .75f, 75f, 1.8f);   // 문짝이 맞물리며 튀는 불티
        Shockwave(Ground(hx), .3f, 3.2f, .4f, 2f);
        Smoke(new Vector3(hx, .3f, 0), new Vector3(2.6f, .3f, 1f), 1.3f, 0, .3f, 1.1f, .6f, 14);
        yield return Hold(.5f);
        foreach (var leaf in leaves) if (leaf) Move(leaf, new Vector3(leaf.position.x + Mathf.Sign(leaf.position.x - hx) * .5f, -1.9f, 0), .32f, null, 1);
        yield return Hold(.3f);
        yield return Settle();
    }

    // 06 채석장 정예병 · 격산비석 (30): 땅에서 바위를 띄워 주먹으로 쳐 날리고, 깨진 파편이 덮친다. 바위가 날아간 자리에서 벗어나면 맞지 않는다.
    IEnumerator FlyingBoulder()
    {
        yield return Gather(.45f, .9f);
        if (foeView != null) foeView.PlaySpecial("LOWKICK"); Pull(.9f, .3f);
        yield return Hold(.18f); Shake(.12f, 6f);
        float rx = FoeX - S * .95f;
        var rock = Rock(new Vector3(rx, -.7f, 0), 1.2f);
        Burst(Ground(rx), .7f); Debris(Ground(rx), 4, 4.5f, .08f, .18f); Cracks(Ground(rx + S * .2f), -S, 1.1f, 2, 30f, .12f, 1.6f);
        Smoke(new Vector3(rx, .3f, 0), new Vector3(1.2f, .3f, 1f), 1.2f, 0, .3f, 1.1f, .7f, 12);
        Move(rock.transform, new Vector3(rx, 1.3f, 0), .22f, null, 2);
        Tween(.34f, u => { if (rock) rock.transform.Rotate(new Vector3(0, 0, 120f * Time.deltaTime), Space.World); });
        yield return Hold(.34f);
        // 주먹으로 친다
        if (foeView != null) foeView.PlaySpecial("CROSS");
        yield return Hold(.15f);
        Fx(qi.muzzle, new Vector3(rx + S * .3f, 1.3f, 0), LookOut, 1.1f); Shake(.12f, 6f); Flash(new Vector3(rx, 1.3f, 0) - Far, 6f, 5f, .25f);
        Sparks(new Vector3(rx + S * .2f, 1.3f, 0), Out, 40, 9f, .6f, 40f, 1.2f);
        Vector3 from = rock.transform.position; float hx = HeroX;
        bool arrived = false;
        Tween(.2f, u => { if (!rock) return; rock.transform.position = Vector3.Lerp(from, new Vector3(hx + S * .4f, 1.2f, 0), u) + Vector3.up * (.35f * Mathf.Sin(u * Mathf.PI)); rock.transform.Rotate(new Vector3(0, 0, 900f * Time.deltaTime), Space.World); }, () => arrived = true);
        Streak(from, new Vector3(hx + S * .4f, 1.3f, 0), .2f, .26f);
        while (!arrived) yield return null;
        // 깨지며 파편이 덮친다
        if (rock) Destroy(rock);
        Vector3 at = new Vector3(hx + S * .3f, 1.2f, 0);
        for (int i = 0; i < 11; i++) Toss(Rock(at + new Vector3(Rand(-.2f, .2f), Rand(-.2f, .2f), Rand(-.2f, .2f)), Rand(.16f, .44f)), new Vector3(S * Rand(-5.5f, 1.5f), Rand(1.5f, 5.5f), Rand(-1.8f, 1.8f)), 2.4f);
        Smoke(at, new Vector3(1.2f, 1.2f, 1f), 1.5f, 0, .3f, 1.2f, .5f, 18); Sparks(at, Out + Vector3.up * .5f, 50, 8f, .8f, 80f, 2f);
        if (Strike(30, Away(6f), 1.2f, .44f, 1.15f, hx, 1.1f)) Launch(.5f, .4f, -22f);
        yield return Hold(.55f);
        yield return Settle();
    }

    // 07 능선 정예병 · 선풍승룡 (6 × 3 + 14 = 32): 제자리에서 돌아 회오리를 보내 띄우고, 뛰어올라 찍는다. 회오리를 막거나 피하면 휘말리지 않는다.
    IEnumerator Whirlwind()
    {
        yield return Gather(.4f, .8f);
        if (foeView != null) foeView.PlaySpecial("HIGHKICK"); Pull(1.6f, .4f);
        Tween(.55f, u => foeSpin = 720f * u, () => foeSpin = 0f);
        // 회오리: 높이마다 다른 빠르기로 도는 바람 줄기들. 위로 갈수록 넓어진다. 마른 붓으로 그은 듯한 결로 그린다.
        float x0 = FoeX - S * .7f, tornadoX = x0, clock = 0f, height = .4f; bool spinning = true;
        const int count = 24;
        var strokes = new LineRenderer[count]; var level = new float[count]; var speed = new float[count]; var span = new float[count]; var start = new float[count]; var wide = new float[count];
        var gust = RibbonMaterial(fx.brushTex, Hdr(Color.Lerp(spec.tint, Color.white, .5f), 1.3f), .4f, 0f, 0f, new Vector4(-2.6f, .1f, 2f, 1f));
        var breeze = RibbonMaterial(fx.brushTex, Hdr(Color.Lerp(spec.tint, Color.white, .5f), .8f), .4f, 0f, 0f, new Vector4(-2.6f, .1f, 2f, 1f));
        for (int i = 0; i < count; i++)
        {
            strokes[i] = Line(i % 4 == 0 ? gust : breeze, 0f, 0f, 4f);
            float w = Rand(.11f, .26f); strokes[i].widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.55f, w), new Keyframe(1f, 0f));
            level[i] = Mathf.Lerp(.08f, 3f, i / (count - 1f)) + Rand(-.06f, .06f); speed[i] = Rand(620f, 1050f); span[i] = Rand(100f, 220f); start[i] = Rand(0f, 360f); wide[i] = Rand(.85f, 1.2f);
        }
        var dust = Smoke(new Vector3(x0, .6f, 0), new Vector3(.8f, 1f, .8f), 1.2f, 36, 1.9f, .8f, 2.6f, 0, .5f);
        updaters.Add(() =>
        {
            if (!spinning) { foreach (var stroke in strokes) if (stroke) stroke.positionCount = 0; return true; }
            clock += Time.deltaTime; height = Mathf.Min(3.1f, height + 7f * Time.deltaTime);
            const int n = 16;
            for (int i = 0; i < count; i++)
            {
                var lr = strokes[i]; if (!lr) continue;
                if (level[i] > height) { lr.positionCount = 0; continue; }
                lr.positionCount = n; float radius = (.2f + .27f * level[i] + .05f * Mathf.Sin(clock * 9f + i)) * wide[i];
                for (int k = 0; k < n; k++)
                {
                    float t = k / (n - 1f), a = (start[i] + clock * speed[i] + t * span[i]) * Mathf.Deg2Rad;
                    lr.SetPosition(k, new Vector3(tornadoX + Mathf.Cos(a) * radius, level[i] + t * .28f, Mathf.Sin(a) * radius));
                }
            }
            if (dust) dust.transform.position = new Vector3(tornadoX, .6f, 0);
            return false;
        });
        yield return Hold(.3f);
        float x1 = HeroX;
        Tween(.42f, u => tornadoX = Mathf.Lerp(x0, x1, u));
        yield return Hold(.42f);
        bool caught = Strike(6, Vector2.zero, .5f, .1f, 1f, x1, 1.1f) && !guardedSpecial;
        if (caught)
        {
            // 휘말려 떠오른다
            Stun(.9f);
            Tween(.5f, u => { heroLift.y = Mathf.Lerp(0f, 1.5f, 1 - (1 - u) * (1 - u)); heroSpin = 600f * u; tornadoX = HeroX; });
            for (int k = 0; k < 2; k++) { yield return Hold(.15f); Strike(6, Vector2.zero, .5f, .1f, 1f + heroLift.y); Stun(.9f); }
            yield return Hold(.15f);
            // 뛰어올라 찍는다
            Slide(gameObject, HeroX + S * .95f, .2f); if (foeView != null) foeView.PlaySpecial("HIGHKICK"); Jump(true, 1.6f, .46f);
            yield return Hold(.24f);
            spinning = false;
            Strike(14, Away(5.5f), 1.1f, .36f, 1.2f + heroLift.y);
            float top = heroLift.y;
            Tween(.13f, u => { heroLift.y = Mathf.Lerp(top, 0f, u * u); heroSpin = 0f; heroTilt = -40f * (1f - u); }, () =>
            {
                heroTilt = 0f; Burst(Ground(HeroX), 1f); Shake(.16f, 6f);
                Smoke(new Vector3(HeroX, .3f, 0), new Vector3(1.4f, .3f, 1.2f), 1.3f, 0, .3f, 1.1f, .6f, 12);
            });
            yield return Hold(.55f);
        }
        else
        {
            yield return Hold(.5f);
            spinning = false;
            yield return Hold(.2f);
        }
        yield return Settle();
    }

    // 08 추포대 추적자 · 삼점봉혈 (3 × 3 + 18 = 27, 2초 공격 봉인): 혈 자리 셋을 찔러 표식을 남기고, 한 박자 뒤 터뜨려 공격을 봉한다. 찔린 만큼만 터진다.
    IEnumerator ThreePoints()
    {
        yield return Gather(.4f, .75f);
        Streak(new Vector3(FoeX, 1.1f, 0), new Vector3(HeroX + S * 1f, 1.1f, 0), .12f, .16f);
        Slide(gameObject, HeroX + S * 1f, .12f);
        yield return Hold(.13f);
        var bones = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest, HumanBodyBones.RightUpperLeg };
        var marks = new List<GameObject>();
        for (int k = 0; k < 3; k++)
        {
            if (foeView != null) foeView.PlaySpecial(k % 2 == 0 ? "JAB" : "CROSS");
            yield return Hold(.1f);
            Vector3 point = HeroBone(bones[k], 1.4f - k * .35f);
            var needle = Wave(Hands(), Quaternion.LookRotation((point - Hands()).normalized, Vector3.up), new Vector3(.1f, .1f, .7f));
            Move(needle.transform, point, .05f, () => EndWave(needle));
            if (Strike(3, Vector2.zero, 0f, .08f, point.y, FoeX, 1.7f) && !guardedSpecial)
            {
                var mark = Fx(qi.ready, point, Quaternion.identity, .6f); var bone = HeroBoneTransform(bones[k]);
                if (mark != null) { if (bone) mark.transform.SetParent(bone, true); marks.Add(mark); }
                Ring(u => point, Vector3.forward, u => Mathf.Lerp(.05f, .34f, u), .3f, .03f, 3f);
                float stay = 1.25f - k * .25f; var pin = bone;
                Ring(u => pin ? pin.position : point, Vector3.forward, u => .1f + .015f * Mathf.Sin(u * 40f), stay, .028f, 2.6f, false);
                Ring(u => pin ? pin.position : point, Vector3.forward, u => .17f, stay, .012f, 2f, false);
                Stun(1.2f);
            }
            yield return Hold(.15f);
        }
        // 물러서서 지켜본다
        Tween(.22f, u => { if (body != null) body.linearVelocity = new Vector2(S * 6f, body.linearVelocity.y); }, () => { if (body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y); });
        if (foeView != null) foeView.PlaySpecial("GUARD_HOLD");
        yield return Hold(.45f);
        if (marks.Count > 0)
        {
            Seal("封", new Vector3(HeroX, 2.0f, 0) + Far * 1.2f, 2.7f, 1.6f);
            yield return Hold(.12f);
            foreach (var mark in marks) { if (mark) { Burst(mark.transform.position, .5f); Sparks(mark.transform.position, Vector3.up, 18, 5f, .5f, 85f, .8f, .04f); Destroy(mark); } }
            Strike(6 * marks.Count, Away(1.5f), .9f, .32f);
            // 봉혈: 몸에 고리가 남아 있는 2초 동안 공격과 파운장이 나가지 않는다(이동과 방어는 된다)
            if (!visualOnly) hero.SealAttacks(2f);
            Ring(u => new Vector3(HeroX, .9f, 0), Vector3.up, u => .46f, 2f, .035f, 2.2f); Ring(u => new Vector3(HeroX, 1.35f, 0), Vector3.up, u => .42f, 2f, .035f, 2.2f);
            yield return Hold(.85f);
        }
        yield return Settle();
    }

    // 09 옥졸장 · 진옥후 (12 + 20 = 32, 가둠): 고함의 음파로 밀어내고, 떨어지는 쇠창살로 가둔다. 고함을 막았으면 창살이 떨어지기 전에 걸어 나올 수 있다.
    IEnumerator PrisonRoar()
    {
        yield return Gather(.6f, 1.1f);
        if (foeView != null) foeView.PlaySpecial("SPECIAL_RELEASE"); Pull(1.2f, .35f);
        Tween(.5f, u => foeTilt = 12f * Mathf.Sin(u * Mathf.PI));
        yield return Hold(.15f);
        // 고함: 입에서 퍼지는 음파 여섯 겹
        Vector3 mouth = new Vector3(FoeX - S * .35f, 1.5f, 0);
        for (int i = 0; i < 6; i++)
        {
            Arc(u => mouth, u => Mathf.Lerp(.3f, 6.5f, u), 52f, .55f, Mathf.Lerp(.11f, .05f, i / 5f));
            Ring(u => mouth + Out * (6f * u), new Vector3(1f, 0f, .5f), u => Mathf.Lerp(.2f, 2.2f, u), .55f, .03f, 2f);
            Shake(.09f, 8f);
            if (i == 1) Smoke(new Vector3(mouth.x - S * 2f, .25f, 0), new Vector3(4f, .2f, 1.4f), 1.1f, 0, .3f, 1f, .5f, 16, .4f);
            if (i == 3 && Strike(12, Away(7f), .7f, .24f, 1.15f, mouth.x, 6.5f) && !guardedSpecial) { Stun(1.6f); heroTilt = -18f; After(.5f, () => heroTilt = 0f); }
            yield return Hold(.06f);
        }
        yield return Hold(.42f);
        // 쇠창살이 떨어져 가둔다: 두드려 만든 검은 쇠창살, 아래는 뾰족하게 박히고 위는 창끝이며 두 군데에 마디가 있다
        float hx = HeroX;
        Seal("獄", new Vector3(hx, 2.1f, 0) + Far * 1.3f, 2.7f, 1.5f);
        var iron = IronMaterial(new Vector2(1f, 5f), Hdr(spec.tint, .2f));
        var shape = new[] { new Vector2(0f, 0f), new Vector2(.22f, .036f), new Vector2(.5f, .036f), new Vector2(.52f, .058f), new Vector2(.6f, .058f), new Vector2(.62f, .036f), new Vector2(2f, .036f), new Vector2(2.02f, .058f), new Vector2(2.1f, .058f), new Vector2(2.12f, .036f), new Vector2(2.32f, .036f), new Vector2(2.36f, .064f), new Vector2(2.44f, .046f), new Vector2(2.66f, 0f) };
        var bars = new List<Transform>();
        for (int i = 0; i < 9; i++)
        {
            float a = (i + .5f) * Mathf.PI * 2 / 9; Vector3 foot = new Vector3(hx + Mathf.Cos(a) * .68f, -.15f, Mathf.Sin(a) * .5f);
            var bar = Lathe(foot + Vector3.up * 6.5f, shape, iron, 12).transform; bars.Add(bar);
            Move(bar, foot, .12f, () => { Burst(new Vector3(foot.x, .05f, foot.z), .3f); Sparks(new Vector3(foot.x, .1f, foot.z), Vector3.up, 12, 5f, .5f, 70f, 1.6f, .04f); Shake(.06f, 8f); }, 1);
            Destroy(bar.gameObject, 2.4f);
            yield return Hold(.03f);
        }
        // 마디를 지나는 쇠테 두 줄이 창살을 묶는다
        foreach (float y in new[] { .41f, 1.91f })
        {
            var hoop = Torus(new Vector3(hx, y, 0), .68f, .03f, iron, 48, 10, 8f).transform; hoop.localScale = new Vector3(1f, 1f, .5f / .68f); bars.Add(hoop);
            Destroy(hoop.gameObject, 2.3f);
            Ring(u => new Vector3(hx, y, 0), Vector3.up, u => .72f, 1.3f, .05f, 2.4f);
        }
        Embers(new Vector3(hx, 1.2f, 0), new Vector3(1.3f, 2.2f, 1f), 40, .9f, 1.5f);
        yield return Hold(.14f);
        // 창살 안에 있으면 갇힌다: 창살이 서 있는 동안 움직이지 못한다
        if (Strike(20, Vector2.zero, 1.1f, .44f, 1.15f, hx, .8f)) Stun(1.2f);
        Flash(new Vector3(hx, 1.3f, 0) - Far, 7f, 5f, .3f);
        yield return Hold(.8f);
        foreach (var bar in bars) if (bar) Move(bar, bar.position + Vector3.down * 3.2f, .25f, null, 1);
        yield return Settle();
    }

    // 10 흑견 · 흑영교 (10 + 10 + 10 = 30, 체력 20% 회복): 그림자가 몸에서 떨어져 나와 달려들어 물고, 기운을 빨아들여 체력을 회복한다. 막거나 피하면 물리지 않는다.
    IEnumerator ShadowBite()
    {
        yield return Gather(.5f, 1.2f, "GUARD_LOWKICK_HOLD");
        float from = FoeX - S * .25f, to = HeroX + S * .7f;
        var shade = Ghost(Ground(from), Ground(HeroX), "WALK_FORWARD", 1.2f, true, 2.4f);
        shade.transform.rotation *= Quaternion.Euler(56f, 0f, 0f);
        var shadeAnim = shade.GetComponentInChildren<Animator>(); var head = shadeAnim != null && shadeAnim.isHuman ? shadeAnim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null) { var eyes = Fx(qi.ready, head.position, Quaternion.identity, .7f); if (eyes != null) eyes.transform.SetParent(head, true); }
        Smoke(new Vector3(from, .6f, 0), new Vector3(.8f, 1f, .7f), 1.1f, 0, .2f, .8f, .4f, 16, .85f);
        var fog = Smoke(new Vector3(from, .45f, 0), new Vector3(.5f, .6f, .5f), 1f, 60, .5f, .7f, .2f, 0, .85f);
        var leash = Line(DarkMaterial, .05f, .17f, 1.4f); var leashGlow = Line(Glow(1.6f), .012f, .03f, 1.4f); float wriggle = 0f;
        Move(shade.transform, new Vector3(to, shade.transform.position.y, 0), .36f, null, 1);
        updaters.Add(() =>
        {
            if (!shade || !leash || !leashGlow) { if (leash) leash.positionCount = 0; if (leashGlow) leashGlow.positionCount = 0; return true; }
            const int n = 16; wriggle += 10f * Time.deltaTime; leash.positionCount = leashGlow.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f); var p = new Vector3(Mathf.Lerp(FoeX - S * .1f, shade.transform.position.x, t), .07f + .04f * Mathf.Sin(t * 12f - wriggle), 0);
                leash.SetPosition(i, p); leashGlow.SetPosition(i, p + Vector3.up * .07f);
            }
            if (fog) fog.transform.position = new Vector3(shade.transform.position.x, .45f, 0);
            return false;
        });
        yield return Hold(.38f);
        // 그림자가 풀어지며 아가리가 된다: 발밑과 머리 위에서 이빨이 맞물린다
        float hx = to - S * .7f; var fangs = new List<GameObject>();
        if (shade) Destroy(shade);
        Smoke(new Vector3(hx + S * .2f, 1f, 0), new Vector3(1.3f, 1.8f, .9f), 1.3f, 24, 1.3f, .8f, .4f, 20, .85f);
        foreach (int jaw in new[] { -1, 1 })
        {
            var line = Line(Glow(2.2f), .035f, .035f, 1.5f); const int n = 20; line.positionCount = n; int sign = jaw;
            Tween(1.4f, u =>
            {
                if (!line) return; float gap = Mathf.Lerp(1.5f, .3f, Mathf.Clamp01(u * 14f)) + .04f * Mathf.Sin(u * 50f);
                for (int i = 0; i < n; i++) { float x = Mathf.Lerp(-.85f, .85f, i / (n - 1f)); line.SetPosition(i, new Vector3(hx + x, 1.15f + sign * (gap + .5f * (1f - x * x / .7225f)), 0)); }
                line.widthMultiplier = 1f - Mathf.Clamp01((u - .8f) / .2f);
            });
        }
        for (int i = -2; i <= 2; i++)
        {
            float lean = i * .09f;
            var lower = Wave(new Vector3(hx + i * .22f, -1.2f, 0), Up, new Vector3(.16f, .16f, 1.0f)); Move(lower.transform, new Vector3(hx + i * .22f - lean, .55f, 0), .09f, null, 1); fangs.Add(lower);
            var upper = Wave(new Vector3(hx + i * .22f, 3.8f, 0), Down, new Vector3(.16f, .16f, 1.0f)); Move(upper.transform, new Vector3(hx + i * .22f - lean, 1.8f, 0), .09f, null, 1); fangs.Add(upper);
        }
        yield return Hold(.09f);
        bool bitten = Strike(10, Vector2.zero, .8f, .24f, 1.15f, hx, 1f) && !guardedSpecial;
        if (bitten)
        {
            Stun(1.8f); Embers(new Vector3(hx, 1.1f, 0), new Vector3(1f, 1.7f, .7f), 34, .8f, 1.3f);
            yield return Hold(.3f);
            Strike(10, Vector2.zero, .6f, .1f);
            // 기운을 빨아들여 회복한다
            Drain(() => HeroChest, () => new Vector3(FoeX, 1.2f, 0), 18, .7f);
            int heal = Mathf.RoundToInt(foeHp.MaxHealth * .2f);
            After(.5f, () => { foeHp.Heal(heal); Fx(qi.ready, new Vector3(FoeX, 1.1f, 0), Quaternion.identity, 3.2f, 1.2f); });
            yield return Hold(.9f);
            foreach (var fang in fangs) EndWave(fang);
            Strike(10, Away(5f), 1.1f, .32f);
        }
        else
        {
            yield return Hold(.4f);
            foreach (var fang in fangs) EndWave(fang);
        }
        yield return Hold(.35f);
        yield return Settle();
    }

    // 11 무진 · 흑운압성 (22 + 3 × 3 + 7 = 38): 하늘이 어두워지고 먹구름이 끌어당긴 뒤, 구름 기둥과 벼락이 떨어진다(구름을 깨는 파운장의 반대말).
    // 모으는 시간이 가장 길어 끊을 틈도 가장 크다. 끌려가는 동안에도 걸어서 버틸 수 있다.
    IEnumerator BlackCloud()
    {
        float mid = (HeroX + FoeX) * .5f;
        Pull(2.6f, .9f);
        Smoke(new Vector3(mid, 3.95f, 0) + Far * .5f, new Vector3(11f, .8f, 3.2f), 3.4f, 54, 3.7f, 2.2f, -.1f, 40, .9f);
        After(.5f, () => Dim(.22f, .9f));
        bool pulling = true;
        updaters.Add(() =>
        {
            if (!pulling || hero == null || heroHp.IsDead) return true;
            if (Mathf.Abs(FoeX - HeroX) > 1.6f) Place(hero.gameObject, HeroX + S * .6f * Time.deltaTime);
            return false;
        });
        for (int i = 0; i < 7; i++)
        {
            After(.7f + i * .2f, () =>
            {
                Vector3 at = new Vector3(mid + Rand(-3.8f, 3.8f), 3.9f, 0) + Far * .3f;
                Puff(at, Rand(2.8f, 4f), .24f); Bolt(at + Vector3.up * .3f, at + new Vector3(Rand(-1.2f, 1.2f), Rand(-.9f, -.4f), 0), .06f, .12f, 1); Flash(at, 5f, 9f, .15f);
            });
        }
        yield return Gather(1.3f, 2.2f);
        pulling = false;
        if (foeView != null) foeView.PlaySpecial("SPECIAL_RELEASE");
        yield return Hold(.18f);
        float hx = HeroX;
        Seal("滅", new Vector3(hx, 2.3f, 0) + Far * 1.5f, 3.6f, 1.7f);
        // 구름 기둥이 내려앉는다
        var column = Wave(new Vector3(hx, 8.5f, 0), Down, new Vector3(1.3f, 1.3f, 3.6f));
        Move(column.transform, new Vector3(hx, 1.7f, 0), .24f, null, 1);
        Smoke(new Vector3(hx, 2.4f, 0), new Vector3(1.7f, 3.8f, 1.3f), 2.1f, 70, .5f, 1f, -2.8f, 24, .9f);
        yield return Hold(.24f);
        EndWave(column); Shake(.34f, 4f);
        if (Strike(22, Vector2.zero, 1.8f, .4f, 1.15f, hx, 1.3f)) Stun(1f);
        Ring(u => new Vector3(hx, .05f, 0), Vector3.up, u => Mathf.Lerp(.4f, 5.2f, u), .42f, .09f, 2.8f); Shockwave(Ground(hx), .4f, 5.4f, .46f, 2.6f);
        Embers(new Vector3(hx, .6f, 0), new Vector3(4f, .8f, 1.6f), 50, 1.8f, 1.7f);
        Cracks(Ground(hx - .2f), -1f, 3f, 4, 36f, .18f); Cracks(Ground(hx + .2f), 1f, 3f, 4, 36f, .18f);
        Debris(Ground(hx), 8, 7f, .14f, .36f);
        Smoke(new Vector3(hx, .4f, 0), new Vector3(3.2f, .4f, 1.6f), 1.9f, 0, .3f, 1.4f, .7f, 26, .8f);
        yield return Hold(.22f);
        // 벼락 세 줄
        for (int k = 0; k < 3; k++)
        {
            float x = hx + (k - 1) * .45f;
            Bolt(new Vector3(x + Rand(-.8f, .8f), 3.9f, 0), new Vector3(x, .1f, 0), .16f, .16f);
            Puff(new Vector3(x, 3.8f, 0), 3.4f, .22f); Burst(new Vector3(x, .9f, 0), 1f); Flash(new Vector3(x, 1.6f, 0) - Far, 12f, 10f, .2f); Shake(.22f, 6f);
            Sparks(new Vector3(x, .12f, 0), Vector3.up, 45, 9f, .7f, 60f, 2f);
            if (Strike(3, Vector2.zero, 0f, .1f, 1.15f, x, 1.2f)) Stun(.6f);
            yield return Hold(.12f);
        }
        if (Strike(7, Away(9f), 1.6f, .5f, 1.15f, hx, 1.6f)) Launch(1.2f, .65f, -36f);
        yield return Hold(.7f);
        yield return Settle();
    }
}
