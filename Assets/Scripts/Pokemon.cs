using System;


[Serializable]
public class Pokemon
{
    public int id; // National Dex (1..)
    public string name; // Display name
    public string[] types; // e.g., ["Grass","Poison"]
    public int generation; // 1..9
    public string sprite; // Resources path (optional), e.g., "Sprites/001"
    public string[] aliases; // accepted alternative guesses
    public int baseId;          // optional
    public string formKey;
}


[Serializable]
public class PokemonList
{
    public Pokemon[] pokemon;
}