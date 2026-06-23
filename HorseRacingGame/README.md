# 🐎 Horse Racing Game

A multiplayer card-based horse racing game built with Blazor Server and SignalR.
Players connect from any device on the local network, get dealt cards, and watch
horses race across the board as dice are rolled. Scratched horses cost you money,
and the winner takes the pot!

## Installation

```bash
cd HorseRacingGame
dotnet restore
```

## How to Run

```bash
dotnet run
```

The app starts on `http://0.0.0.0:5000` and is accessible from any device on
your local network.

## How Other Players Connect

Other players on the same network open a browser and navigate to:

```
http://[YOUR_IP]:5000
```

### Finding Your Local IP Address

**Windows:**
1. Open Command Prompt
2. Run `ipconfig`
3. Look for "IPv4 Address" under your active network adapter
   (usually something like `192.168.1.x`)

**Mac:**
1. Open Terminal and run `ifconfig`
2. Look for `inet` under `en0` (Wi-Fi) or `en1`
3. Or go to System Preferences > Network and check your connected adapter

Share the URL with your friends and they can join from phones, tablets, or laptops.

## Game Rules

### Setup
- 2 to 8 players supported
- Each player starts with $100
- A 44-card deck is used (standard 52-card deck minus Aces, Kings, and Jokers)
- 11 horses are numbered 2 through 12, matching possible dice sums
- Each horse has a different track length based on dice probability:
  - Horse 2: 2 spaces | Horse 3: 5 | Horse 4: 7 | Horse 5: 8
  - Horse 6: 12 | **Horse 7: 16** | Horse 8: 12
  - Horse 9: 8 | Horse 10: 7 | Horse 11: 5 | Horse 12: 2

### Phases

1. **Lobby** — Players join and the host starts the game
2. **Dealing** — All 44 cards are dealt round-robin to players
3. **Scratching** — Players take turns rolling dice. The rolled horse number
   gets scratched (eliminated). 4 horses are scratched total. Players holding
   cards for that horse pay a penalty into the pot:
   - 1st scratch: $5/card
   - 2nd scratch: $3/card
   - 3rd scratch: $2/card
   - 4th scratch: $1/card
4. **Racing** — Players take turns rolling dice. The matching horse advances
   one space. If a scratched horse is rolled, the roller pays the scratch
   penalty into the pot.
5. **Payout** — When a horse reaches its finish line, the pot is split equally
   among all players holding active cards for that horse number.

### Winning
- The player(s) holding cards matching the winning horse number split the pot
- Pot is divided per card (more cards = bigger share)
- Game can be played multiple rounds — balance carries over

## Supported Players

**2 to 8 players** — all connected via the local network in real time.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 8 |
| UI | Blazor Server (Razor Components) |
| Real-time | SignalR (built into ASP.NET Core) |
| Language | C# |
| Styling | CSS + Bootstrap 5 (CDN) |
| State | Singleton in-memory GameService |
| Transport | WebSockets via Kestrel |

No external APIs, no database, no cloud services — everything runs locally in memory.
Game state resets when the server restarts.
