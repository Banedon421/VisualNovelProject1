using System;
using System.Collections.Generic;

// The player character's identity. Deliberately small: clan/pole details
// are looked up by id in the data definitions (ClanDefinition, PoleDefinition)
// rather than duplicated here.
[Serializable]
public class PlayerProfile
{
    public string characterName;
    public string clanId;
    public string currentPoleId; // can change during play, unlike clanId
    public int birthYear;
    public List<string> personalTraits = new List<string>();
}
