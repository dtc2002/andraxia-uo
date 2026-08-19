# Andraxia Development Instructions

## Project Intent

Andraxia is a low-population Ultima Online shard built on ModernUO.

The primary design goal is a persistent living world that remains interesting with approximately 1–10 concurrent human players.

Development should preserve the strengths of Ultima Online while adding systems that make the world feel active, reactive, explorable, and persistent without requiring a large player population.

## Authority

ModernUO upstream behavior and architecture should be preserved unless an Andraxia requirement explicitly requires modification.

Prefer extension over modification.

Do not modify ModernUO core code merely for convenience.

## Development Rules

1. Prefer existing ModernUO APIs, patterns, abstractions, and conventions.
2. Keep Andraxia-specific behavior isolated and clearly identifiable.
3. Avoid unrelated refactors.
4. Do not introduce frameworks or abstractions unless they solve a demonstrated requirement.
5. Keep feature changes small and independently reviewable.
6. Do not silently change existing ModernUO gameplay behavior.
7. Do not modify persistence formats casually.
8. Do not modify production world/save data.
9. Do not commit generated build output or runtime save data.
10. Do not introduce external dependencies without explaining why they are necessary.
11. Preserve compatibility with upstream ModernUO updates wherever practical.
12. Prefer deterministic server-authoritative systems over opaque behavior.
13. Performance matters. Systems intended to simulate a living world must scale gracefully when no players are nearby.
14. Avoid continuously simulating large populations of full Mobile instances when abstract simulation can accomplish the same result.
15. Every persistent system must define initialization, save/load behavior, recovery behavior, and administrative inspection/reset capability.

## Testing Requirements

Every implementation must:

1. Build successfully.
2. Run applicable existing tests.
3. Add automated tests for deterministic business/gameplay logic where practical.
4. Avoid requiring a running production shard for basic logic validation.
5. Include regression tests when fixing defects.
6. Clearly identify behavior that requires live shard testing.

Andraxia will maintain a test harness for simulation-heavy systems.

The test harness should eventually support controlled testing of:

- persistent world state
- state transitions
- dynamic events
- event scaling
- NPC simulation
- economic simulation
- contracts and bounties
- treasure generation
- progression systems
- persistence round-trips
- long-duration simulated time

## Git Rules

- `main` represents known-good Andraxia.
- New work should normally occur on feature branches.
- Do not commit broken builds to `main`.
- Keep commits focused.
- Do not rewrite upstream ModernUO history.
- `upstream` refers to the official ModernUO repository.
- `origin` refers to the private Andraxia repository.

## Deployment Rules

Source development occurs outside the production shard.

Production should receive published build artifacts, not become the primary development working tree.

Deployment must never overwrite runtime world/save data unless explicitly performing a restore or migration.

Production deployment should eventually require:

1. clean Git state
2. successful build
3. successful automated tests
4. successful publish
5. backup/preflight verification
6. controlled server stop
7. deployment
8. controlled restart
9. startup verification

## Scope Discipline

Do not implement future roadmap features merely because they appear useful.

Implement only the requested work package.

When a requested feature would benefit from a broader reusable abstraction, explain the opportunity before implementing the broader abstraction.

## Design Principle

Andraxia systems should interact through well-defined shared concepts rather than direct dependencies wherever practical.

Before creating a feature-specific mechanism, ask whether the underlying capability should be reusable by other living-world systems.

## Production Safety

Never:

- delete world saves
- reset accounts
- alter production persistence
- expose credentials
- change network configuration
- deploy directly to production
- run destructive migrations

without explicit human instruction.