Investigate the multiplayer architecture for Washed Ashore: whether Unity Gaming Services (UGS) fits, and whether to use a hybrid of player-hosted and dedicated servers. **This is an investigation only:** no networking code on main.

## What we know (checked 2026-10-14)

- **The direction (README):** an open-world survival RPG with permadeath and a persistent world. "A server model with limited population and a persistent world is currently the preferred direction." Multiplayer shares the solo core loop.
- **The project has no networking packages** (no Netcode, Transport or Multiplayer Services). It's on **Unity 6000.6**, URP. **Targets: Windows, Linux and macOS** (#14; there are no web players, so no WebSocket constraint).
- **Unity's dedicated hosting is gone.** Multiplay Game Server Hosting was **deprecated on 1 April 2026**, and new allocations stopped ([status notice 362941](https://status.unity.com/info_notices/362941)). Unity licensed the software to **Rocket Science Group** for live titles ([docs](https://docs.unity.com/en-us/multiplay-hosting)).
- **The other services are unaffected.** Unity staff confirm "only Multiplay Hosting is impacted" for Relay, Lobby, Matchmaker and Distributed Authority, and that Matchmaker works with third-party hosts ([discussions](https://discussions.unity.com/t/end-of-services/1698428)).
- **The current Netcode needs 6000.7.** Netcode for GameObjects **3.1.0** (4 Oct 2026) and Netcode for Entities **7.1.0** (3 Oct 2026) both require Unity **6000.7**. On 6000.6 we're limited to NGO 2.13.x and Netcode for Entities 6.6/6.7. The Distributed Authority GA status isn't confirmed; the NGO 3.1 manual no longer labels it beta.
- **Systems that will need networking:**
  - the player controller
  - interactions (the lookout hatch, sash and firefinder)
  - wildlife, birds and fish (fish and birds are seeded per cell and partly deterministic; water motion is a pure function, per the water spec)
  - permadeath and saving

## Phase 0: research (deep-recon, select shape)

The output is `_bmad-output/research/technical-multiplayer-<date>/research.md`, with a weighted decision matrix.

## Pass criteria (what makes this investigation complete)

| # | Criterion |
|---|---|
| Q1 | **An architecture matrix:** dedicated authoritative server, player host with Relay, Distributed Authority, and hybrids (for example player-hosted small sessions plus dedicated persistent worlds). Scored against: persistence, permadeath integrity, anti-cheat, cost and complexity, all cited |
| Q2 | **A services pick:** which UGS pieces to use or avoid (Authentication, Lobby, Relay, Matchmaker, Cloud Save, Distributed Authority), and what replaces dedicated hosting (Multiplay by Rocket Science, Edgegap, Gameye, self-hosted Linux, or others). Each with **current pricing pages read directly (≤ 3 months old)**, lock-in, Linux server support and region coverage |
| Q3 | **Netcode for GameObjects or Entities,** at our scale. Includes a recommendation on **whether to upgrade to Unity 6000.7**, with the upgrade's risk to the existing suites |
| Q4 | **What the server must own:** authority, the persistence store and save cadence, and the permadeath rules. Plus how the server's world-loading interacts with #12 |
| Q5 | **Monthly cost** at three scales: 1 world of 16 players, 5 worlds, and 20 worlds |
| Q6 | **A POC spec,** in the format of the other POC specs: "two players walk the same world and see each other". It's to be run as a separate issue |

**Gate:** technical-director recommends, game-director confirms that it fits the design, and Noah picks.

## Automatic FAIL

- Networking code on main.
- Cost figures without dated sources.
- Ignoring the 6000.7 version constraint.

## Pod

**technical-director** (lead), **platform-tools-engineer** (hosting and cost) and **systems-designer** (persistence and permadeath needs): 3 agents.

Related: #12, #14, #15 and #16.
