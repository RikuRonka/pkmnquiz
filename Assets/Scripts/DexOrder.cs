using System.Collections.Generic;

public static class DexOrder
{
    static readonly int[] Gen8Order = new int[]
    {
        810,
        811,
        812,
        813,
        814,
        815,
        816,
        817,
        818,
        819,
        820,
        821,
        822,
        823,
        824,
        825,
        826,
        827,
        828,
        829,
        830,
        831,
        832,
        833,
        834,
        835,
        836,
        837,
        838,
        839,
        840,
        841,
        842,
        843,
        61802,
        844,
        845,
        846,
        847,
        848,
        849,
        850,
        851,
        852,
        853,
        7702,
        7802,
        854,
        855,
        856,
        857,
        858,
        859,
        860,
        861,
        11002,
        26302,
        26402,
        862,
        5202,
        863,
        22202,
        864,
        8302,
        865,
        12202,
        866,
        56202,
        867,
        868,
        869,
        870,
        871,
        872,
        873,
        55402,
        55502,
        874,
        875,
        876,
        877,
        878,
        879,
        880,
        881,
        882,
        883,
        884,
        885,
        886,
        887,
        888,
        889,
        890,
        7902,
        8002,
        19902,
        891, // Kubfu
        892, // Urshifu
        893, // Zarude
        894, // Regieleki
        895, // Regidrago
        14402, // Galarian Articuno
        14502, // Galarian Zapdos
        14602, // Galarian Moltres
        896, // Glastrier
        897, // Spectrier
        898, // Calyrex
    };
    static readonly Dictionary<int, int> Gen8Index = BuildIndex(Gen8Order);

    static readonly int[] HisuiOrder = new int[]
    {
        // 1) New Hisui species first
        210724, // Hisuian Decidueye
        210157, // Hisuian Typhlosion
        210503, // Hisuian Samurott
        899, // Wyrdeer
        900, // Kleavor
        210549, // Hisuian Lilligant
        901, // Ursaluna
        210705, // Hisuian Sliggoo
        210706, // Hisuian Goodra
        210058, // Hisuian Growlithe
        210059, // Hisuian Arcanine
        55000, // Basculin white-striped
        902, // Basculegion
        210100, // Hisuian Voltorb
        210101, // Hisuian Electrode
        210215, // Hisuian Sneasel
        903, // Sneasler
        210211, // Hisuian Qwilfish
        904, // Overqwil
        210713, // Hisuian Avalugg
        210570, // Hisuian Zorua
        210571, // Hisuian Zoroark
        210628, // Hisuian Braviary
        905, // Enamorus
        211, // Hisuian Qwilfish
        210483, // Dialga (Origin Forme)
        210484, // Palkia (Origin Forme)
    };
    static readonly Dictionary<int, int> HisuiIndex = BuildIndex(HisuiOrder);

    public static int GetIndex(Pokemon p)
    {
        // Hisui block first (new mons + regional forms + Origin forms)
        if (IsHisuiRelated(p))
        {
            if (HisuiIndex.TryGetValue(p.id, out var idx))
                return 20000 + idx; // after base Galar, before Gmax
            return 29999; // unknown Hisui → bottom of Hisui block
        }

        // Galar / Gmax block
        if (IsGen8NonHisui(p))
        {
            if (Gen8Index.TryGetValue(p.id, out var idx))
                return 10000 + idx;
            return 19999;
        }

        // everything else
        return 30000 + p.id;
    }

    static bool IsHisuiRelated(Pokemon p)
    {
        // Things that visually live in the Hisui row:
        if (Helpers.IsHisui(p))
            return true; // all Hisuian forms

        if (p.id >= 899 && p.id <= 905)
            return true; // Wyrdeer → Enamorus

        if (p.id == 210483 || p.id == 210484)
            return true; // Origin Dialga/Palkia

        return false;
    }

    static bool IsGen8NonHisui(Pokemon p)
    {
        // Pure Galar + Gmax, but NOT Hisui
        if (p.generation != 8)
            return false;

        if (IsHisuiRelated(p))
            return false; // don’t double-count

        return true;
    }

    static Dictionary<int, int> BuildIndex(int[] ids)
    {
        var map = new Dictionary<int, int>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
            map[ids[i]] = i;
        return map;
    }
}
