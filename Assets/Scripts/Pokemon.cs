using System;

[Serializable]
public class Pokemon
{
    public int id;
    public string name;
    public string[] types;
    public PokemonEvolution evolution;
    public int generation;
    public string sprite;
    public string[] aliases;
    public int baseId;
    public string baseSpecies;
    public string formKey;
    public string dlcKey;
}

[Serializable]
public class PokemonEvolution
{
    public int stage;
    public int totalStages;
    public string[] line;
    public string[][] paths;
    public bool isFinal;
    public bool hasBranches;
}

[Serializable]
public class PokemonList
{
    public Pokemon[] pokemon;
}
