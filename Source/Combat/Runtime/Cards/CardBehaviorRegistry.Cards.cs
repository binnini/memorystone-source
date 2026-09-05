using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    public static partial class CardBehaviorRegistry
    {
        // 카드당 한 줄. cards.csv 순서(type · id). 새 카드 = 클래스 파일 하나 + 여기 한 줄.
        private static IEnumerable<CardBehavior> CreateAll()
        {
            yield return new M01_Move1Hex();
            yield return new M02_Move2Hex();
            yield return new M03_Move3Hex();
            yield return new M04_Move4Hex();
            yield return new M05_Momentum();
            yield return new M06_RandomJourney();
            yield return new M07_Shortcut();
            yield return new M08_FullSprint();
            yield return new A00_BasicStrike();
            yield return new A01_Sweep();
            yield return new A02_MoveLinkedStrike();
            yield return new A03_HolyLight();
            yield return new A04_FinishingTouch();
            yield return new A05_FinalBlow();
            yield return new A06_DoubleHit();
            yield return new A07_OneStrikeEnough();
            yield return new A08_MultiplyingStrike();
            yield return new A09_MultiplyingStrikeCopy();
            yield return new A10_Sacrifice();
            yield return new A11_TargetShot();
            yield return new A12_Plague();
            yield return new A13_Remnant();
            yield return new A14_Intimidate();
            yield return new D00_BasicBlock();
            yield return new D01_OldArmor();
            yield return new D02_ShelterTaunt();
            yield return new D03_DoubleEdgedShield();
            yield return new D04_HeavyArmor();
            yield return new D05_TalismanShield();
            yield return new D06_Hospitalization();
            yield return new D07_FullyPrepared();
            yield return new S00_BasicScout();
            yield return new S01_Minefinder();
            yield return new S02_Treasurefinder();
            yield return new S03_StunFlash();
            yield return new S04_Bingo();
            yield return new S05_StoneBridgeTap();
            yield return new S06_WeakSpot();
            yield return new F01_Firebomb();
            yield return new F02_SacredLamp();
            yield return new F03_Flashbang();
            yield return new F04_LifestealZone();
            yield return new F05_BounceBomb();
            yield return new U01_Redraw();
            yield return new U02_DrawOrRecover();
            yield return new U03_CleanseDraw();
            yield return new U04_Torch();
            yield return new X01_FineDust();
            yield return new X02_BrokenGlass();
            yield return new X03_Blackout();
            yield return new X04_DebtNote();
            yield return new X05_Nightmare();
            yield return new X06_Lingering();
            yield return new X07_Tardiness();
            yield return new X08_Nuisance();
            yield return new X09_CursedCharm();
            yield return new X10_MurkyFog();
            yield return new X11_GoblinPrank();
            yield return new X12_VengefulGhost();
        }
    }
}
