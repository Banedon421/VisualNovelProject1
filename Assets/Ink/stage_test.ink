// stage_test.ink
// Exercises every character-related function built so far, in one scene.
// Scene JSON: stage_test.json (empty "characters" list -- both dupont and
// jeanne are brought on screen via enter(), not pre-placed, so entering
// itself gets tested too).
//
// Point GameBootstrapper.sceneDefinitionFileName at "stage_test.json" in
// the Inspector to run this.

INCLUDE shared_externals.ink

-> stage_test

=== stage_test ===
This is a test of every stage-direction function built so far.

~ enter("dupont", "off_left")
~ jump_to("dupont", "left")
Dupont fades in on the left and slides into position. # speaker:dupont

~ enter("jeanne", "off_right")
~ jump_to("jeanne", "right")
Jeanne fades in on the right and slides into position. # speaker:jeanne

Notice how, once Jeanne finishes arriving, she's full opacity and Dupont dims slightly -- that's the speaker: tag on the line above driving CharacterView's highlight/dim, separate from anything below.

-> test_choices

=== test_choices ===
+ [Test switch_mood] -> test_mood
+ [Test turn_around / face] -> test_facing
+ [Test highlight] -> test_highlight
+ [Test jump_to_instant / set_depth] -> test_depth
+ [Test fade_to / fade_in / fade_out] -> test_fade
+ [Test exit / re-enter] -> test_exit
+ [Test per-character queue ordering] -> test_queue
* [Finish test] -> stage_test_end

=== test_mood ===
~ switch_mood("dupont", "worried")
Dupont's portrait should now show a "worried" mood. # speaker:dupont

If you haven't added a dupont_worried.jpg to Resources/Portraits/ yet, check the Console -- you should see a clear warning naming the missing file, and Dupont should stay on his neutral portrait rather than going blank.
-> test_choices

=== test_facing ===
~ turn_around("dupont")
Dupont just flipped to face the opposite way from before. # speaker:dupont

~ face("jeanne", "left")
Jeanne is now explicitly facing left, regardless of whatever turn_around would have toggled her to. # speaker:jeanne

~ turn_around("dupont")
And flipped back, so repeated testing doesn't leave him backwards.
-> test_choices

=== test_highlight ===
~ highlight("jeanne")
Jeanne should have popped slightly, as if she just spoke -- independent of the speaker: dimming, which only changes opacity, not scale. # speaker:jeanne
-> test_choices

=== test_depth ===
~ jump_to_instant("dupont", "center")
Dupont snapped instantly to center -- no slide, unlike jump_to. # speaker:dupont

~ set_depth("dupont", 0)
Dupont should now render BEHIND Jeanne if they overlap (sibling index 0 = first = furthest back).

~ jump_to("dupont", "left")
...and back to his spot, this time sliding normally.
-> test_choices

=== test_fade ===
~ fade_out("jeanne")
Jeanne fades out completely...

~ fade_in("jeanne")
...and back in, both using the default fade duration.

~ fade_to("jeanne", 0.4, 1.5)
Now easing down to 40% transparency over a slow 1.5 seconds -- fade_to gives full control over both target and duration, in seconds.

~ fade_to("jeanne", 1, 0.5)
...then back to fully visible, quickly this time.
-> test_choices

=== test_exit ===
~ exit("dupont")
Dupont fades out and leaves entirely. Try picking another option quickly right after this one -- anything targeting "dupont" should log a clear warning instead of doing anything, since he's genuinely off screen now, not just invisible.

~ enter("dupont", "left")
...and here he is again, freshly entered rather than resurrected.
-> test_choices

=== test_queue ===
This tests the ordering guarantee itself: two instructions for the SAME character, fired back to back, with no chance to see the first one finish before the second is called.

~ turn_around("jeanne")
~ exit("jeanne")
If the queue is working, Jeanne fully completes the turn (instant) BEFORE her exit fade even starts -- not simultaneously, not skipped.

~ enter("jeanne", "off_right")
~ jump_to("jeanne", "right")
And brought back for the rest of the test. Meanwhile, watch Dupont during all of this -- he should be completely unaffected, since queueing is per-character.
-> test_choices

=== stage_test_end ===
Test complete.
-> END
