// town_square.ink
// Scene: town_square.json points here.

INCLUDE shared_externals.ink

VAR player_clan = ""

-> square_intro

=== square_intro ===
~ player_clan = get_clan()

The town square of Ashford is busier than usual this morning. A small crowd has gathered near the well.

{
- player_clan == "grue":
    Word of your arrival travels fast among the Crane -- a few merchants bow their heads as you pass.
- player_clan == "crabe":
    A pair of Crab guards straighten up when they notice you, though nobody bows.
- else:
    Nobody here seems entirely sure what to make of you yet.
}

Dupont the blacksmith and Jeanne the farmer are arguing quietly by the well. They stop when they see you.

Dupont: "Ah -- perfect timing, actually." # speaker:dupont

Jeanne: "Don't drag them into this, Dupont." # speaker:jeanne

Dupont: "It concerns everyone. Grain prices, again." # speaker:dupont

-> square_choices

=== square_choices ===
+ [Ask what the argument is about] -> argument_explained
+ [Ask Jeanne how the harvest is going] -> harvest_check
* [Try to leave before getting pulled into this] -> try_leave

=== argument_explained ===
~ add_relationship("npc:dupont", 1)
~ add_relationship("npc:jeanne", 1)

Jeanne: "The forge is buying less iron ore than it used to. Dupont says it's the market. I say it's the army stockpiling." # speaker:jeanne

{
- get_pole() == "army":
    Dupont glances at you sideways. "No offense meant, if that's your lot." # speaker:dupont
- get_pole() == "administration":
    Jeanne: "Maybe you could look into it. You'd know better than us." # speaker:jeanne
- else:
    Dupont shrugs. "Politics. Not my trade, usually." # speaker:dupont
}

* [Offer to look into the grain situation] -> offer_help
* [Say it's not your place to get involved] -> stay_out

=== harvest_check ===
{ not has_flag("harvest_checked"):
    ~ add_relationship("npc:jeanne", 1)
    ~ set_flag("harvest_checked")
}

{ get_stat("production") < 30:
    Jeanne: "Thin. Thinner than I'd like to admit in front of Dupont." # speaker:jeanne
    ~ set_flag("knows_weak_harvest")
- else:
    Jeanne: "Better than last year, at least. Small mercies." # speaker:jeanne
}

-> square_choices

=== offer_help ===
~ set_flag("offered_grain_help")
~ add_relationship("npc:dupont", 2)
~ add_relationship("npc:jeanne", 2)
~ add_stat("stability", 3)

Dupont: "Well. Didn't expect that, but I won't argue." # speaker:dupont

Jeanne: "We'll hold you to it." # speaker:jeanne

-> square_ending

=== stay_out ===
~ add_stat("stability", -1)

Dupont: "Fair enough. Wasn't really your mess to begin with." # speaker:dupont

Jeanne says nothing, just watches you a moment longer than feels comfortable. # speaker:jeanne

-> square_ending

=== try_leave ===
Jeanne: "Running off already?" # speaker:jeanne

Dupont: "Can't blame them. I'd run too." # speaker:dupont

-> square_ending

=== square_ending ===
~ advance_years(0)

You leave the square behind, the argument still unresolved behind you.
-> END
