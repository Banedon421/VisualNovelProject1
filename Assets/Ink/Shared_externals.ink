// shared_externals.ink
//
// EXTERNAL declarations for every game-facing function bound in
// InkBinder.cs. INCLUDE this at the top of any .ink file that calls any
// of them -- one line instead of restating the whole list every time.
//
// This file is a pure fragment, never a root story: it has no content of
// its own (no knots, no diverts) and is never pointed at directly by a
// SceneDefinition's "inkFile". InkStreamingAssetsExporter.cs already
// expects this -- a file with no compiled JSON of its own is treated as
// "not an error, just an INCLUDE-d fragment," not something broken.
//
// Keep this list in sync with InkBinder.cs by hand, same as always --
// story.ValidateExternalBindings() (called by GameBootstrapper right
// after Bind()) still catches any drift between the two, PER FUNCTION,
// exactly as before. INCLUDE doesn't weaken that safety net at all; it
// only removes the need to retype the list in every file.

// --- World state / player (WorldState, via InkBinder) ---
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

// --- Stage control (StageDirector, via InkBinder) ---
EXTERNAL jump_to(character_id, position_name)
EXTERNAL jump_to_instant(character_id, position_name)
EXTERNAL enter(character_id, position_name)
EXTERNAL exit(character_id)
EXTERNAL turn_around(character_id)
EXTERNAL face(character_id, direction)
EXTERNAL switch_mood(character_id, mood)
EXTERNAL highlight(character_id)
EXTERNAL set_depth(character_id, order)
EXTERNAL fade_to(character_id, target_alpha, duration)
EXTERNAL fade_in(character_id)
EXTERNAL fade_out(character_id)
