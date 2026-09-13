// main.ink
// Entry point for the whole story. Split into more INCLUDE-d files as it
// grows -- this single file is only meant to prove the pipeline works.

EXTERNAL get_stat(stat_id)
EXTERNAL add_stat(stat_id, amount)
EXTERNAL get_relationship(target_id)
EXTERNAL add_relationship(target_id, amount)
EXTERNAL get_clan()
EXTERNAL get_pole()
EXTERNAL set_pole(pole_id)
EXTERNAL has_flag(flag_id)
EXTERNAL set_flag(flag_id)
EXTERNAL get_year()
EXTERNAL advance_years(years)

VAR player_clan = ""

-> village_intro

=== village_intro ===
~ player_clan = get_clan()

{
- player_clan == "grue":
    The village of Ashford flies the pale banners of the Crane, and morning mist clings to painted eaves.
- player_clan == "crabe":
    The village of Ashford is walled in grey stone, Crab sentries pacing the ramparts even at dawn.
- else:
    The village of Ashford wakes slowly under a clan banner you barely recognize yet.
}

You approach the forge, where Dupont the blacksmith is already at work.

{
- player_clan == "grue":
    Dupont: "Another fine morning for the Crane, isn't it? Mind the ash on the road, we swept it not an hour ago." # speaker:dupont
- player_clan == "crabe":
    Dupont: "Storm's coming off the coast. Crab folk don't wait for fine mornings, we work through them." # speaker:dupont
- else:
    Dupont: "Morning. Forge's hot if you need anything." # speaker:dupont
}

-> village_choices

=== village_choices ===
* [Ask Dupont about the harvest] -> harvest_talk
* [Ask about tensions in the capital] -> capital_talk
* [Leave the village] -> leave_village

=== harvest_talk ===
~ add_relationship("npc:dupont", 1)
{ get_stat("production") < 30:
    Dupont: "Truth is, we're struggling. Fields gave less than half of last year." # speaker:dupont
    ~ add_stat("stability", -2)
- else:
    Dupont: "Can't complain. Storehouses are full enough." # speaker:dupont
}
-> village_choices

=== capital_talk ===
{
- get_pole() == "army":
    Dupont: "You lot in the army keep talking about requisitions. Hope you're not here for our grain." # speaker:dupont
- get_pole() == "administration":
    Dupont: "Administration folk usually bring paperwork, not trouble. What do you need?" # speaker:dupont
- else:
    Dupont: "Politics is above my forge." # speaker:dupont
}
-> village_choices

=== leave_village ===
You leave the forge behind, {get_year()} years into your story so far.
-> END
