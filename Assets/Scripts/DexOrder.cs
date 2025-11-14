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

    static readonly int[] AlolaOrder = new int[]
    {
        // Rowlet line
        722,
        723,
        724,
        // Litten line
        725,
        726,
        727,
        // Popplio line
        728,
        729,
        730,
        // Pikipek line
        731,
        732,
        733,
        // Yungoos line
        734,
        735,
        // --- INSERT ALOLAN VARIANTS (Rattata-A, Raticate-A) HERE ---
        1901,
        2001,
        // --- INSERT ALOLAN VARIANTS (Raichu-A) HERE ---
        2601,
        // Grubbin line
        736,
        737,
        738,
        5201,
        5301,
        8801,
        8901,
        // Crabrawler
        739,
        740,
        5001,
        5101,
        // Oricorio forms are SAME ID (741) → no variants needed
        741,
        // Cutiefly line
        742,
        743,
        // Rockruff → Lycanroc (3 forms share IDs 745)
        744,
        745,
        // Wishiwashi
        746,
        // Mareanie line
        747,
        748,
        // Mudbray line
        749,
        750,
        // Dewpider line
        751,
        752,
        // Fomantis line
        753,
        754,
        // Morelull line
        755,
        756,
        // Salandit line
        757,
        758,
        10501,
        // Stufful line
        759,
        760,
        // Bounsweet line
        761,
        762,
        763,
        // Comfey
        764,
        // Oranguru
        765,
        // Passimian
        766,
        // Wimpod line
        767,
        768,
        // Sandygast line
        769,
        770,
        // Pyukumuku
        771,
        // Type: Null → Silvally
        772,
        773,
        // Minior
        774,
        // Komala
        775,
        // Turtonator
        776,
        // Togedemaru
        777,
        7401,
        7501,
        7601,
        // Mimikyu
        778,
        // Bruxish
        779,
        // Drampa
        780,
        2701,
        2801,
        3701,
        3801,
        // Dhelmise
        781,
        10301,
        // Jangmo-o line
        782,
        783,
        784,
        // Tapu guardians
        785,
        786,
        787,
        788,
        // Cosmog line
        789,
        790,
        791,
        792,
        793,
        794,
        795,
        796,
        797,
        798,
        799,
        // Necrozma
        800,
        // Magearna
        801,
        // Marshadow
        802,
        // Poipole line
        803,
        804,
        // Naganadel
        // (same as above)

        // Stakataka
        805,
        // Blacephalon
        806,
        // Zeraora
        807,
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
        if (p.generation == 7)
            return GetAlolaIndex(p.id);
        // everything else
        return 30000 + p.id;
    }

    static int GetAlolaIndex(int id)
    {
        for (int i = 0; i < AlolaOrder.Length; i++)
            if (AlolaOrder[i] == id)
                return i;

        return 99999; // anything missing goes to the end
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
