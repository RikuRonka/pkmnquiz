using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(CanvasGroup))]
public class PokemonTooltip : MonoBehaviour
{
    [Header("Wiring")]
    private TMP_Text nameLabel;

    [SerializeField]
    private TMP_Text descriptionText;

    [SerializeField]
    private CanvasGroup cg;

    [SerializeField]
    private LayoutElement layoutElement;

    [Header("Sizing")]
    [SerializeField]
    private float minWidth = 260f;

    [SerializeField]
    private float maxWidth = 800f;

    [SerializeField]
    private float contentPadding = 40f;

    public float pokemonMaxWidth = 520f;

    [SerializeField]
    private VerticalLayoutGroup vlg;

    private const float PokemonMinTooltipWidth = 160f;
    private const float PokemonMaxTooltipWidth = 340f;
    private const float EvolutionItemWidth = 52f;
    private const float EvolutionItemHeight = 68f;
    private const float EvolutionSpriteSize = 36f;
    private const float EvolutionTypeIconSize = 12f;
    private const float EvolutionTypeIconRowHeight = 12f;
    private const float EvolutionArrowWidth = 14f;
    private const float EvolutionRowSpacing = 4f;
    private const int EvolutionEntriesPerRow = 4;

    private RectTransform tooltipPanelRect;
    private RectTransform contentRootRect;
    private RectTransform evolutionStackRect;
    private TMP_Text titleText;
    private TMP_Text notesText;
    private TMP_Text singleStageText;
    private Image tooltipBackground;
    private VerticalLayoutGroup panelLayout;
    private VerticalLayoutGroup contentLayout;
    private VerticalLayoutGroup evolutionStackLayout;
    private ContentSizeFitter panelSizeFitter;
    private ContentSizeFitter contentSizeFitter;
    private LayoutElement contentLayoutElement;
    private LayoutElement evolutionStackElement;
    private float evolutionPreferredWidth;
    private float evolutionPreferredHeight;

    public bool IsVisible => cg && cg.alpha > 0.001f;
    public RectTransform VisualRoot => tooltipPanelRect ? tooltipPanelRect : transform as RectTransform;

    public Vector2 PreferredSize
    {
        get
        {
            ForceTooltipLayout();

            var rt = VisualRoot;
            if (rt && rt.rect.width > 0f && rt.rect.height > 0f)
                return rt.rect.size;

            if (!rt)
                return Vector2.zero;

            float w = LayoutUtility.GetPreferredWidth(rt);
            float h = LayoutUtility.GetPreferredHeight(rt);
            return new Vector2(w, h);
        }
    }

    private void Awake()
    {
        BuildRuntimeHierarchy();
        SetVisible(false, immediate: true);
    }

    private void BuildRuntimeHierarchy()
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);

        var outerImage = GetComponent<Image>();
        if (outerImage)
        {
            outerImage.enabled = false;
            outerImage.raycastTarget = false;
        }

        cg = GetComponent<CanvasGroup>();
        if (!cg)
            cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        tooltipPanelRect = CreateRectChild(transform, "TooltipPanel");
        tooltipBackground = tooltipPanelRect.gameObject.AddComponent<Image>();
        tooltipBackground.color = new Color(0f, 0f, 0f, 1f);
        tooltipBackground.raycastTarget = false;

        panelLayout = tooltipPanelRect.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.childAlignment = TextAnchor.MiddleCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = false;
        panelLayout.childForceExpandHeight = false;
        panelLayout.spacing = 0f;
        panelLayout.padding = new RectOffset(0, 0, 0, 0);

        panelSizeFitter = tooltipPanelRect.gameObject.AddComponent<ContentSizeFitter>();
        panelSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        panelSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        contentRootRect = CreateRectChild(tooltipPanelRect, "ContentRoot");
        contentLayout = contentRootRect.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.childAlignment = TextAnchor.MiddleCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = false;
        contentLayout.spacing = 5f;
        contentLayout.padding = new RectOffset(8, 8, 8, 8);

        contentSizeFitter = contentRootRect.gameObject.AddComponent<ContentSizeFitter>();
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        contentLayoutElement = contentRootRect.gameObject.AddComponent<LayoutElement>();
        contentLayoutElement.flexibleWidth = 0f;
        contentLayoutElement.flexibleHeight = 0f;

        layoutElement = contentLayoutElement;
        vlg = contentLayout;

        titleText = CreateTextChild(contentRootRect, "TitleText");
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        titleText.fontSize = 20f;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 12f;
        titleText.fontSizeMax = 20f;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.raycastTarget = false;
        SetLayout(titleText.gameObject, preferredHeight: 24f);

        nameLabel = titleText;

        notesText = CreateTextChild(contentRootRect, "DescriptionText");
        notesText.alignment = TextAlignmentOptions.TopLeft;
        notesText.color = Color.white;
        notesText.fontSize = 14f;
        notesText.textWrappingMode = TextWrappingModes.Normal;
        notesText.overflowMode = TextOverflowModes.Overflow;
        notesText.raycastTarget = false;
        notesText.gameObject.SetActive(false);
        descriptionText = notesText;

        evolutionStackRect = CreateRectChild(contentRootRect, "EvolutionLine");
        evolutionStackLayout = evolutionStackRect.gameObject.AddComponent<VerticalLayoutGroup>();
        evolutionStackLayout.childAlignment = TextAnchor.MiddleCenter;
        evolutionStackLayout.childControlWidth = true;
        evolutionStackLayout.childControlHeight = true;
        evolutionStackLayout.childForceExpandWidth = false;
        evolutionStackLayout.childForceExpandHeight = false;
        evolutionStackLayout.spacing = EvolutionRowSpacing;
        evolutionStackLayout.padding = new RectOffset(0, 0, 0, 0);

        evolutionStackElement = evolutionStackRect.gameObject.AddComponent<LayoutElement>();
        evolutionStackElement.flexibleWidth = 0f;
        evolutionStackElement.flexibleHeight = 0f;

        singleStageText = CreateTextChild(evolutionStackRect, "SingleStageText");
        singleStageText.alignment = TextAlignmentOptions.Center;
        singleStageText.color = new Color(1f, 1f, 1f, 0.92f);
        singleStageText.fontSize = 13f;
        singleStageText.enableAutoSizing = true;
        singleStageText.fontSizeMin = 9f;
        singleStageText.fontSizeMax = 13f;
        singleStageText.textWrappingMode = TextWrappingModes.NoWrap;
        singleStageText.overflowMode = TextOverflowModes.Ellipsis;
        singleStageText.raycastTarget = false;
        SetLayout(singleStageText.gameObject, preferredHeight: 18f);
        singleStageText.gameObject.SetActive(false);

        HideEvolutionContent();
        ForceTooltipLayout();
    }

    public void SetContent(string name, string type1, string type2)
    {
        SetContent(name, type1, type2, null, null, null);
    }

    public void SetContent(
        string name,
        string type1,
        string type2,
        Pokemon pokemon,
        IReadOnlyCollection<int> guessedIds,
        IReadOnlyCollection<int> activeQuizIds
    )
    {
        tooltipBackground.color = new Color(0f, 0f, 0f, 1f);
        titleText.text = name ?? string.Empty;
        titleText.gameObject.SetActive(true);
        notesText.gameObject.SetActive(false);

        ApplyEvolutionContent(pokemon, guessedIds, activeQuizIds);

        float titleWidth = titleText.GetPreferredValues(titleText.text, PokemonMaxTooltipWidth, 0f).x;
        float contentWidth = Mathf.Max(titleWidth, evolutionPreferredWidth);
        float targetWidth = Mathf.Clamp(
            contentWidth + contentLayout.padding.left + contentLayout.padding.right,
            PokemonMinTooltipWidth,
            Mathf.Min(PokemonMaxTooltipWidth, pokemonMaxWidth)
        );

        ApplyWidth(targetWidth);
    }

    public void SetNotes(string title, string rawNotes)
    {
        tooltipBackground.color = new Color(0f, 0f, 0f, 1f);
        titleText.text = title ?? string.Empty;
        titleText.gameObject.SetActive(true);
        HideEvolutionContent();

        notesText.gameObject.SetActive(true);
        notesText.alignment = TextAlignmentOptions.TopLeft;
        notesText.textWrappingMode = TextWrappingModes.Normal;
        notesText.text = FormatUpdateNotes(rawNotes);

        float contentWidth = MeasureWideNotes(notesText);
        float targetWidth = Mathf.Clamp(
            contentWidth + contentPadding,
            minWidth,
            GetMaxScreenTooltipWidth()
        );
        ApplyWidth(targetWidth);
    }

    public void SetPlayerList(string title, IReadOnlyList<string> playerNames)
    {
        tooltipBackground.color = new Color(0f, 0f, 0f, 1f);
        titleText.text = title ?? string.Empty;
        titleText.gameObject.SetActive(true);
        HideEvolutionContent();

        notesText.gameObject.SetActive(true);
        notesText.alignment = TextAlignmentOptions.TopLeft;
        notesText.textWrappingMode = TextWrappingModes.Normal;
        notesText.text =
            playerNames != null && playerNames.Count > 0
                ? string.Join("\n", playerNames)
                : "No players";

        float contentWidth = MeasureWideNotes(notesText);
        float targetWidth = Mathf.Clamp(
            contentWidth + contentPadding,
            minWidth,
            GetMaxScreenTooltipWidth()
        );
        ApplyWidth(targetWidth);
    }

    public void SetVisible(bool visible, bool immediate, float duration = 0.1f)
    {
        if (!cg)
            return;

        StopAllCoroutines();
        if (immediate)
            cg.alpha = visible ? 1f : 0f;
        else
            StartCoroutine(FadeCo(visible ? 1f : 0f, duration));
    }

    public bool ConstrainWidth(float maxAllowedWidth)
    {
        if (!layoutElement || maxAllowedWidth <= 0f)
            return false;

        float current =
            layoutElement.preferredWidth > 0f ? layoutElement.preferredWidth : PreferredSize.x;
        float target = Mathf.Max(1f, Mathf.Min(current, maxAllowedWidth));
        bool changed =
            !Mathf.Approximately(layoutElement.preferredWidth, target)
            || layoutElement.minWidth < 0f
            || layoutElement.minWidth > target;

        if (!changed)
            return false;

        ApplyWidth(target);
        return true;
    }

    private void ApplyEvolutionContent(
        Pokemon pokemon,
        IReadOnlyCollection<int> guessedIds,
        IReadOnlyCollection<int> activeQuizIds
    )
    {
        ClearEvolutionRows();
        evolutionPreferredWidth = 0f;
        evolutionPreferredHeight = 0f;

        if (
            pokemon == null
            || guessedIds == null
            || pokemon.evolution == null
            || !guessedIds.Contains(pokemon.id)
        )
        {
            HideEvolutionContent();
            return;
        }

        evolutionStackRect.gameObject.SetActive(true);

        if (pokemon.evolution.totalStages == 1)
        {
            singleStageText.gameObject.SetActive(true);
            singleStageText.text = "Single-stage Pokémon";
            AddSingleStageTypeIcons(pokemon);
            AddSpecialFormRows(pokemon, guessedIds, activeQuizIds);
            SetEvolutionStackSize();
            return;
        }

        singleStageText.gameObject.SetActive(false);

        var paths = GetEvolutionPaths(pokemon, activeQuizIds);
        if (paths.Count == 0)
            paths.Add(new[] { pokemon.name });

        if (paths.Count == 1)
            AddEvolutionPathRows(paths[0], guessedIds, pokemon);
        else if (!TryAddGroupedBranchRows(paths, guessedIds, pokemon))
            AddEvolutionPathRowsForEachBranch(paths, guessedIds, pokemon);

        AddSpecialFormRows(pokemon, guessedIds, activeQuizIds);
        SetEvolutionStackSize();
    }

    private static List<string[]> GetEvolutionPaths(
        Pokemon pokemon,
        IReadOnlyCollection<int> activeQuizIds
    )
    {
        List<string[]> paths;

        if (pokemon?.evolution?.paths != null && pokemon.evolution.paths.Length > 0)
        {
            paths = pokemon.evolution.paths
                .Where(path => path != null && path.Length > 0)
                .Select(path => path.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray())
                .Where(path => path.Length > 0)
                .ToList();
        }
        else if (pokemon?.evolution?.line != null && pokemon.evolution.line.Length > 0)
        {
            paths = new List<string[]> { pokemon.evolution.line };
        }
        else
        {
            paths = new List<string[]> { new[] { pokemon?.name ?? string.Empty } };
        }

        return FilterEvolutionPathsToActiveQuiz(paths, activeQuizIds, pokemon);
    }

    private static List<string[]> FilterEvolutionPathsToActiveQuiz(
        List<string[]> paths,
        IReadOnlyCollection<int> activeQuizIds,
        Pokemon currentPokemon
    )
    {
        if (paths == null || paths.Count == 0)
            return new List<string[]>();

        if (activeQuizIds == null || activeQuizIds.Count == 0)
            return DeduplicateAndRemovePrefixPaths(paths);

        var active = new HashSet<int>(activeQuizIds);
        if (currentPokemon != null)
            active.Add(currentPokemon.id);

        var filtered = new List<string[]>();
        foreach (var path in paths)
        {
            if (path == null || path.Length == 0)
                continue;

            var names = new List<string>();
            foreach (string name in path)
            {
                var p = FindPokemonByEvolutionName(name);
                if (p != null && active.Contains(p.id))
                    names.Add(p.name);
            }

            if (names.Count > 0)
                filtered.Add(names.ToArray());
        }

        return DeduplicateAndRemovePrefixPaths(filtered);
    }

    private static List<string[]> DeduplicateAndRemovePrefixPaths(List<string[]> paths)
    {
        var deduped = new List<string[]>();
        var seen = new HashSet<string>();

        foreach (var path in paths)
        {
            if (path == null || path.Length == 0)
                continue;

            string key = string.Join(
                "\u001F",
                path.Select(name => (name ?? string.Empty).Trim().ToLowerInvariant())
            );
            if (seen.Add(key))
                deduped.Add(path);
        }

        return deduped
            .Where((path, index) =>
                !deduped.Where((other, otherIndex) => otherIndex != index)
                    .Any(other => IsStrictPrefix(path, other))
            )
            .ToList();
    }

    private static bool IsStrictPrefix(string[] candidatePrefix, string[] fullPath)
    {
        if (
            candidatePrefix == null
            || fullPath == null
            || candidatePrefix.Length >= fullPath.Length
        )
            return false;

        for (int i = 0; i < candidatePrefix.Length; i++)
        {
            if (
                !string.Equals(
                    candidatePrefix[i],
                    fullPath[i],
                    System.StringComparison.OrdinalIgnoreCase
                )
            )
                return false;
        }

        return true;
    }

    private void AddEvolutionPathRows(
        string[] names,
        IReadOnlyCollection<int> guessedIds,
        Pokemon currentPokemon
    )
    {
        int entriesInRow = 0;
        RectTransform row = null;

        for (int i = 0; i < names.Length; i++)
        {
            if (!row || entriesInRow >= EvolutionEntriesPerRow)
            {
                row = AddEvolutionRow();
                entriesInRow = 0;
            }

            if (entriesInRow > 0)
                AddEvolutionArrow(row);

            AddEvolutionEntry(row, names[i], guessedIds, currentPokemon);
            entriesInRow++;
        }
    }

    private void AddEvolutionPathRowsForEachBranch(
        List<string[]> paths,
        IReadOnlyCollection<int> guessedIds,
        Pokemon currentPokemon
    )
    {
        foreach (var path in paths)
            AddEvolutionPathRows(path, guessedIds, currentPokemon);
    }

    private bool TryAddGroupedBranchRows(
        List<string[]> paths,
        IReadOnlyCollection<int> guessedIds,
        Pokemon currentPokemon
    )
    {
        int prefixLength = GetCommonPrefixLength(paths);
        if (prefixLength <= 0)
            return false;

        if (paths.Any(path => path.Length != prefixLength + 1))
            return false;

        var prefix = paths[0].Take(prefixLength).ToArray();
        var branchNames = new List<string>();
        foreach (var path in paths)
        {
            string branchName = path[prefixLength];
            if (
                !branchNames.Any(existing =>
                    string.Equals(
                        existing,
                        branchName,
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
            )
                branchNames.Add(branchName);
        }

        if (branchNames.Count == 0)
            return false;

        int branchIndex = 0;
        bool firstRow = true;
        while (branchIndex < branchNames.Count)
        {
            var row = AddEvolutionRow();
            int entriesInRow = 0;

            if (firstRow)
            {
                for (int i = 0; i < prefix.Length; i++)
                {
                    if (entriesInRow > 0)
                        AddEvolutionArrow(row);

                    AddEvolutionEntry(row, prefix[i], guessedIds, currentPokemon);
                    entriesInRow++;
                }

                if (entriesInRow > 0)
                    AddEvolutionArrow(row);
            }

            int capacity = firstRow
                ? Mathf.Max(1, EvolutionEntriesPerRow - entriesInRow)
                : EvolutionEntriesPerRow;
            for (int i = 0; i < capacity && branchIndex < branchNames.Count; i++)
            {
                AddEvolutionEntry(row, branchNames[branchIndex], guessedIds, currentPokemon);
                branchIndex++;
            }

            firstRow = false;
        }

        return true;
    }

    private static int GetCommonPrefixLength(List<string[]> paths)
    {
        if (paths == null || paths.Count < 2)
            return 0;

        int minLength = paths.Min(path => path.Length);
        int prefixLength = 0;

        for (int i = 0; i < minLength; i++)
        {
            string first = paths[0][i];
            bool allMatch = paths.All(path =>
                string.Equals(first, path[i], System.StringComparison.OrdinalIgnoreCase)
            );
            if (!allMatch)
                break;

            prefixLength++;
        }

        return prefixLength;
    }

    private void AddEvolutionEntry(
        RectTransform row,
        string pokemonName,
        IReadOnlyCollection<int> guessedIds,
        Pokemon currentPokemon
    )
    {
        var linePokemon = FindPokemonByEvolutionName(pokemonName);
        bool guessed =
            linePokemon != null
            && (guessedIds.Contains(linePokemon.id) || linePokemon.id == currentPokemon.id);
        bool current =
            linePokemon != null
                ? linePokemon.id == currentPokemon.id
                : string.Equals(
                    pokemonName,
                    currentPokemon.name,
                    System.StringComparison.OrdinalIgnoreCase
                );

        AddEvolutionEntry(row, linePokemon, guessed, current);
    }

    private void AddSpecialFormRows(
        Pokemon currentPokemon,
        IReadOnlyCollection<int> guessedIds,
        IReadOnlyCollection<int> activeQuizIds
    )
    {
        if (currentPokemon == null)
            return;

        var all = PokemonDatabase.Instance.All();
        var basePokemon = FindBasePokemonForForms(currentPokemon, all);
        if (basePokemon == null)
            return;

        HashSet<int> active = null;
        if (activeQuizIds != null && activeQuizIds.Count > 0)
            active = new HashSet<int>(activeQuizIds);

        var forms = all.Where(p =>
                IsSpecialForm(p)
                && IsFormOfBase(p, basePokemon)
                && p.id != basePokemon.id
                && (active == null || active.Contains(p.id))
            )
            .OrderBy(p => DexOrder.GetIndex(p))
            .ThenBy(p => p.id)
            .ToList();

        if (forms.Count == 0)
            return;

        AddSectionLabel(forms.All(IsMegaForm) ? "Mega forms" : "Forms");

        int formIndex = 0;
        bool firstRow = true;
        while (formIndex < forms.Count)
        {
            var row = AddEvolutionRow();
            int entriesInRow = 0;

            if (firstRow)
            {
                bool baseGuessed =
                    guessedIds.Contains(basePokemon.id) || currentPokemon.id == basePokemon.id;
                bool baseCurrent = currentPokemon.id == basePokemon.id;
                AddEvolutionEntry(row, basePokemon, baseGuessed, baseCurrent);
                AddEvolutionArrow(row);
                entriesInRow = 2;
            }

            int capacity = firstRow
                ? Mathf.Max(1, EvolutionEntriesPerRow - entriesInRow)
                : EvolutionEntriesPerRow;
            for (int i = 0; i < capacity && formIndex < forms.Count; i++)
            {
                var form = forms[formIndex];
                bool formGuessed = guessedIds.Contains(form.id) || currentPokemon.id == form.id;
                bool formCurrent = currentPokemon.id == form.id;
                AddEvolutionEntry(
                    row,
                    form,
                    formGuessed,
                    formCurrent,
                    GetSpecialFormDisplayName(form)
                );
                formIndex++;
            }

            firstRow = false;
        }
    }

    private TMP_Text AddSectionLabel(string text)
    {
        var label = CreateTextChild(evolutionStackRect, "SectionLabel");
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 1f, 1f, 0.72f);
        label.fontStyle = FontStyles.Bold;
        label.fontSize = 10f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.text = text ?? string.Empty;
        SetLayout(label.gameObject, preferredHeight: 12f);
        return label;
    }

    private static Pokemon FindBasePokemonForForms(Pokemon pokemon, IReadOnlyList<Pokemon> all)
    {
        if (pokemon == null)
            return null;

        if (pokemon.baseId != 0)
        {
            var byId = all.FirstOrDefault(p => p.id == pokemon.baseId);
            if (byId != null)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(pokemon.baseSpecies))
        {
            var byBaseSpecies = PokemonDatabase.Instance.FindByGuess(pokemon.baseSpecies);
            if (byBaseSpecies != null)
                return byBaseSpecies;
        }

        return pokemon;
    }

    private static bool IsFormOfBase(Pokemon form, Pokemon basePokemon)
    {
        if (form == null || basePokemon == null)
            return false;

        if (form.baseId != 0 && form.baseId == basePokemon.id)
            return true;

        return !string.IsNullOrWhiteSpace(form.baseSpecies)
            && string.Equals(
                form.baseSpecies,
                basePokemon.name,
                System.StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsSpecialForm(Pokemon pokemon)
    {
        return IsMegaForm(pokemon) || Helpers.IsGmax(pokemon);
    }

    private static bool IsMegaForm(Pokemon pokemon)
    {
        return Helpers.IsMega(pokemon)
            || Helpers.IsLumioseMega(pokemon)
            || Helpers.IsHyperspaceMega(pokemon);
    }

    private static string GetSpecialFormDisplayName(Pokemon pokemon)
    {
        if (pokemon == null)
            return string.Empty;

        if (pokemon.aliases != null)
        {
            string megaAlias = pokemon.aliases.FirstOrDefault(alias =>
                !string.IsNullOrWhiteSpace(alias)
                && alias.StartsWith("Mega ", System.StringComparison.OrdinalIgnoreCase)
            );
            if (!string.IsNullOrWhiteSpace(megaAlias))
                return megaAlias;
        }

        return pokemon.name;
    }

    private RectTransform AddEvolutionRow()
    {
        var row = CreateRectChild(evolutionStackRect, "EvolutionRow");
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = EvolutionRowSpacing;
        hlg.padding = new RectOffset(0, 0, 0, 0);

        SetLayout(row.gameObject, preferredHeight: EvolutionItemHeight);
        return row;
    }

    private void AddEvolutionEntry(
        RectTransform row,
        Pokemon pokemon,
        bool guessed,
        bool current,
        string labelOverride = null
    )
    {
        var item = CreateRectChild(row, "EvolutionItem");
        var bg = item.gameObject.AddComponent<Image>();
        bg.color = current ? new Color(1f, 0.88f, 0.25f, 0.28f) : new Color(1f, 1f, 1f, 0.08f);
        bg.raycastTarget = false;

        var outline = item.gameObject.AddComponent<Outline>();
        outline.enabled = current;
        outline.effectColor = new Color(1f, 0.9f, 0.28f, 0.95f);
        outline.effectDistance = new Vector2(1f, -1f);

        var v = item.gameObject.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.MiddleCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;
        v.spacing = 1f;
        v.padding = new RectOffset(2, 2, 2, 1);

        SetLayout(
            item.gameObject,
            preferredWidth: EvolutionItemWidth,
            preferredHeight: EvolutionItemHeight,
            minWidth: EvolutionItemWidth,
            minHeight: EvolutionItemHeight
        );

        var spriteBox = CreateRectChild(item, guessed ? "SpriteImage" : "UnknownBox");
        var spriteImage = spriteBox.gameObject.AddComponent<Image>();
        spriteImage.raycastTarget = false;
        spriteImage.preserveAspect = true;
        SetLayout(
            spriteBox.gameObject,
            preferredWidth: EvolutionSpriteSize,
            preferredHeight: EvolutionSpriteSize,
            minWidth: EvolutionSpriteSize,
            minHeight: EvolutionSpriteSize
        );

        TMP_Text unknownText = null;
        if (!guessed)
        {
            spriteImage.color = new Color(1f, 1f, 1f, 0.12f);
            unknownText = CreateTextChild(spriteBox, "QuestionMark");
            unknownText.alignment = TextAlignmentOptions.Center;
            unknownText.color = Color.white;
            unknownText.fontStyle = FontStyles.Bold;
            unknownText.fontSize = 18f;
            unknownText.text = "?";
            StretchToParent((RectTransform)unknownText.transform);
        }

        var label = CreateTextChild(item, "NameText");
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 10f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 7f;
        label.fontSizeMax = 10f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        SetLayout(label.gameObject, preferredWidth: EvolutionItemWidth - 4f, preferredHeight: 12f);

        if (guessed && pokemon != null)
        {
            spriteImage.sprite = SpriteLibrary.Instance.ByPokemon(pokemon);
            spriteImage.enabled = spriteImage.sprite != null;
            spriteImage.color = Color.white;
            label.text = string.IsNullOrWhiteSpace(labelOverride) ? pokemon.name : labelOverride;
            AddEvolutionTypeIcons(item, pokemon);
        }
        else
        {
            spriteImage.sprite = null;
            spriteImage.enabled = true;
            if (unknownText)
                unknownText.text = "?";
            label.text = string.Empty;
        }
    }

    private void AddEvolutionTypeIcons(RectTransform item, Pokemon pokemon)
    {
        var sprites = GetTypeIconSprites(pokemon);
        if (sprites.Count == 0)
            return;

        AddTypeIconRow(item, "TypeIcons", sprites, EvolutionItemWidth - 4f);
    }

    private float AddSingleStageTypeIcons(Pokemon pokemon)
    {
        var sprites = GetTypeIconSprites(pokemon);
        if (sprites.Count == 0)
            return 0f;

        float width =
            sprites.Count * EvolutionTypeIconSize + Mathf.Max(0, sprites.Count - 1) * 2f;
        AddTypeIconRow(
            evolutionStackRect,
            "SingleStageTypeIcons",
            sprites,
            Mathf.Max(width, EvolutionTypeIconSize)
        );

        return width;
    }

    private static List<Sprite> GetTypeIconSprites(Pokemon pokemon)
    {
        var sprites = new List<Sprite>();
        if (pokemon?.types == null || pokemon.types.Length == 0)
            return sprites;

        for (int i = 0; i < pokemon.types.Length; i++)
        {
            var sprite = TypeIconLibrary.Instance.Get(pokemon.types[i]);
            if (sprite)
                sprites.Add(sprite);
        }

        return sprites;
    }

    private RectTransform AddTypeIconRow(
        Transform parent,
        string objectName,
        IReadOnlyList<Sprite> sprites,
        float preferredWidth
    )
    {
        var typeRow = CreateRectChild(parent, objectName);
        var hlg = typeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.spacing = 2f;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        SetLayout(
            typeRow.gameObject,
            preferredWidth: preferredWidth,
            preferredHeight: EvolutionTypeIconRowHeight
        );

        for (int i = 0; i < sprites.Count; i++)
        {
            var iconRect = CreateRectChild(typeRow, "TypeIcon");
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = sprites[i];
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = Color.white;
            SetLayout(
                iconRect.gameObject,
                preferredWidth: EvolutionTypeIconSize,
                preferredHeight: EvolutionTypeIconSize,
                minWidth: EvolutionTypeIconSize,
                minHeight: EvolutionTypeIconSize
            );
        }

        return typeRow;
    }

    private void AddEvolutionArrow(RectTransform row)
    {
        var arrow = CreateTextChild(row, "ArrowText");
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.color = new Color(1f, 1f, 1f, 0.68f);
        arrow.fontSize = 11f;
        arrow.text = "->";
        arrow.textWrappingMode = TextWrappingModes.NoWrap;
        SetLayout(arrow.gameObject, preferredWidth: EvolutionArrowWidth, preferredHeight: EvolutionItemHeight);
    }

    private void SetEvolutionStackSize()
    {
        int activeChildCount = 0;
        float maxWidth = 0f;
        float totalHeight = 0f;

        for (int i = 0; i < evolutionStackRect.childCount; i++)
        {
            var child = evolutionStackRect.GetChild(i) as RectTransform;
            if (!child || !child.gameObject.activeSelf)
                continue;

            float childWidth = GetStackChildPreferredWidth(child);
            float childHeight = GetStackChildPreferredHeight(child);

            if (child.TryGetComponent<HorizontalLayoutGroup>(out _))
                SetLayout(child.gameObject, preferredWidth: childWidth, preferredHeight: childHeight);

            maxWidth = Mathf.Max(maxWidth, childWidth);
            totalHeight += childHeight;
            activeChildCount++;
        }

        if (activeChildCount > 1)
            totalHeight += (activeChildCount - 1) * EvolutionRowSpacing;

        evolutionPreferredWidth = maxWidth;
        evolutionPreferredHeight = totalHeight;

        SetLayout(
            evolutionStackRect.gameObject,
            preferredWidth: Mathf.Max(1f, evolutionPreferredWidth),
            preferredHeight: Mathf.Max(1f, evolutionPreferredHeight)
        );
    }

    private static float GetStackChildPreferredWidth(RectTransform child)
    {
        if (!child)
            return 0f;

        if (child.TryGetComponent<HorizontalLayoutGroup>(out _))
            return CalculateRowWidth(child);

        if (child.TryGetComponent<TMP_Text>(out var text))
            return text.GetPreferredValues(text.text).x;

        if (child.TryGetComponent<LayoutElement>(out var element))
            return Mathf.Max(element.minWidth, element.preferredWidth);

        return Mathf.Max(0f, child.rect.width);
    }

    private static float GetStackChildPreferredHeight(RectTransform child)
    {
        if (!child)
            return 0f;

        if (child.TryGetComponent<LayoutElement>(out var element))
            return Mathf.Max(element.minHeight, element.preferredHeight);

        if (child.TryGetComponent<TMP_Text>(out var text))
            return text.GetPreferredValues(text.text).y;

        return Mathf.Max(0f, child.rect.height);
    }

    private static float CalculateRowWidth(RectTransform row)
    {
        float width = 0f;
        int activeChildren = 0;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i).gameObject;
            if (!child.activeSelf)
                continue;

            activeChildren++;
            if (child.TryGetComponent<LayoutElement>(out var element))
                width += Mathf.Max(element.minWidth, element.preferredWidth);
        }

        if (activeChildren > 1)
            width += (activeChildren - 1) * EvolutionRowSpacing;

        return width;
    }

    private void ClearEvolutionRows()
    {
        if (!evolutionStackRect)
            return;

        for (int i = evolutionStackRect.childCount - 1; i >= 0; i--)
        {
            var child = evolutionStackRect.GetChild(i);
            if (child == singleStageText.transform)
                continue;

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        singleStageText.gameObject.SetActive(false);
    }

    private void HideEvolutionContent()
    {
        ClearEvolutionRows();
        evolutionPreferredWidth = 0f;
        evolutionPreferredHeight = 0f;

        if (evolutionStackRect)
            evolutionStackRect.gameObject.SetActive(false);
    }

    private void ApplyWidth(float preferred)
    {
        preferred = Mathf.Max(1f, preferred);
        contentLayoutElement.preferredWidth = preferred;
        contentLayoutElement.minWidth = Mathf.Min(preferred, maxWidth);
        contentLayoutElement.preferredHeight = -1f;
        contentLayoutElement.minHeight = -1f;

        ForceTooltipLayout();
    }

    private void ForceTooltipLayout()
    {
        if (evolutionStackRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(evolutionStackRect);
        if (contentRootRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRootRect);
        if (tooltipPanelRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanelRect);

        Canvas.ForceUpdateCanvases();

        if (contentRootRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRootRect);
        if (tooltipPanelRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipPanelRect);
    }

    private float GetMaxScreenTooltipWidth()
    {
        var c = GetComponentInParent<Canvas>();
        if (c == null)
            return maxWidth;

        var rt = c.transform as RectTransform;
        if (rt == null)
            return maxWidth;

        return Mathf.Max(200f, rt.rect.width - 50f);
    }

    private float MeasureWideNotes(TMP_Text t)
    {
        if (t == null)
            return minWidth;

        float screenLimit = GetMaxScreenTooltipWidth();
        var pref = t.GetPreferredValues(t.text, screenLimit, 0);
        return Mathf.Min(pref.x, screenLimit);
    }

    private System.Collections.IEnumerator FadeCo(float target, float d)
    {
        float start = cg.alpha;
        float t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0, 1, t / d));
            yield return null;
        }
        cg.alpha = target;
    }

    private static Pokemon FindPokemonByEvolutionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var p = PokemonDatabase.Instance.FindByGuess(name);
        if (p != null)
            return p;

        return PokemonDatabase.Instance.All()
            .FirstOrDefault(x =>
                string.Equals(x.name, name, System.StringComparison.OrdinalIgnoreCase)
            );
    }

    private RectTransform CreateRectChild(Transform parent, string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        ResetRectTransform(rect);
        return rect;
    }

    private TMP_Text CreateTextChild(Transform parent, string objectName)
    {
        var rect = CreateRectChild(parent, objectName);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        return text;
    }

    private static void ResetRectTransform(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static LayoutElement SetLayout(
        GameObject go,
        float preferredWidth = -1f,
        float preferredHeight = -1f,
        float minWidth = -1f,
        float minHeight = -1f
    )
    {
        var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        element.ignoreLayout = false;
        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
        element.minWidth = minWidth;
        element.minHeight = minHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        if (go.transform is RectTransform rect)
        {
            float width = preferredWidth > 0f ? preferredWidth : minWidth;
            float height = preferredHeight > 0f ? preferredHeight : minHeight;
            Vector2 size = rect.sizeDelta;

            if (width > 0f)
                size.x = width;
            if (height > 0f)
                size.y = height;

            rect.sizeDelta = size;
        }

        return element;
    }

    private static string FormatUpdateNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        var s = notes.Replace("\r", "").Trim();
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"^\s*v?\d+(?:\.\d+){1,3}\s*(?:[-:]\s*|\n+)",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        var parts = System.Text.RegularExpressions.Regex.Split(
            s,
            @"(?m)^\s*[-\u2022]\s+|\s+-\s+|\n+"
        );
        var cleaned = new List<string>();
        foreach (var part in parts)
        {
            var item = System.Text.RegularExpressions.Regex.Replace(part.Trim(), @"\s*\n\s*", " ");
            if (!string.IsNullOrWhiteSpace(item))
                cleaned.Add("\u2022 " + item);
        }

        return string.Join("\n", cleaned);
    }
}
