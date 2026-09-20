// demo_counter.ink
// Demonstrates dynamic text + repeatable choices + Unity EXTERNAL functions.

EXTERNAL get_relationship(target_id)
EXTERNAL add_relationship(target_id, amount)

-> start

=== start ===
You currently have { get_relationship("npc:dupont") } relationship points with Dupont.

What do you want to do?

+ [Increase relationship by 2] -> add_points
+ [Increase relationship by 1] -> add_small
* [Stop this dialog] -> end_dialog

=== add_points ===
~ add_relationship("npc:dupont", 2)
-> start

=== add_small ===
~ add_relationship("npc:dupont", 1)
-> start

=== end_dialog ===
Thanks for testing! Your final relationship score is { get_relationship("npc:dupont") }.
-> END
