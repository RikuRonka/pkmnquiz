using System;

[Serializable]
public class Pokemon
{
    public int id;
    public string name;
    public string[] types;
    public int generation;
    public string sprite;
    public string[] aliases;
    public int baseId;
    public string formKey;
    public string dlcKey;
}

[Serializable]
public class PokemonList
{
    public Pokemon[] pokemon;
}
