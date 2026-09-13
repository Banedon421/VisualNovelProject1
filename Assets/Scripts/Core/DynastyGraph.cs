using System;
using System.Collections.Generic;
using System.Linq;

public enum LifeStatus { Alive, Dead }

// A single node in the family tree. Kept as plain data so it serializes
// easily; DynastyGraph below owns the relationships between nodes.
[Serializable]
public class FamilyMember
{
    public string id;
    public string displayName;
    public string clanId;
    public int birthYear;
    public int? deathYear;
    public LifeStatus status = LifeStatus.Alive;
    public string spouseId;
    public string parentAId;
    public string parentBId;
    public List<string> childrenIds = new List<string>();
    public List<string> traits = new List<string>(); // e.g. "has_magic_talent"
}

// The family/dynasty structure: romances, marriages, births, deaths,
// inheritance. Kept separate from RelationshipSystem because these are
// structural facts (who is related to whom), not a mood/affinity scalar.
public class DynastyGraph
{
    private readonly Dictionary<string, FamilyMember> _members = new Dictionary<string, FamilyMember>();

    public FamilyMember Get(string id) => _members.TryGetValue(id, out var m) ? m : null;

    public IEnumerable<FamilyMember> AllMembers => _members.Values;
    public IEnumerable<FamilyMember> LivingMembers => _members.Values.Where(m => m.status == LifeStatus.Alive);

    public FamilyMember AddMember(string id, string displayName, string clanId, int birthYear)
    {
        var member = new FamilyMember { id = id, displayName = displayName, clanId = clanId, birthYear = birthYear };
        _members[id] = member;
        return member;
    }

    public void Marry(string idA, string idB)
    {
        var a = Get(idA);
        var b = Get(idB);
        if (a == null || b == null) return;
        a.spouseId = idB;
        b.spouseId = idA;
    }

    public FamilyMember AddChild(string parentAId, string parentBId, string id, string displayName, int birthYear)
    {
        var child = AddMember(id, displayName, Get(parentAId)?.clanId, birthYear);
        child.parentAId = parentAId;
        child.parentBId = parentBId;
        Get(parentAId)?.childrenIds.Add(id);
        Get(parentBId)?.childrenIds.Add(id);
        return child;
    }

    public void Kill(string id, int deathYear)
    {
        var member = Get(id);
        if (member == null) return;
        member.status = LifeStatus.Dead;
        member.deathYear = deathYear;
    }

    public List<FamilyMember> Snapshot() => _members.Values.ToList();

    public void Restore(List<FamilyMember> members)
    {
        _members.Clear();
        foreach (var m in members) _members[m.id] = m;
    }
}
