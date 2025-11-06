TODO:
- main menu hover buttons some effect
- always scroll to pokemon (no matter is it guessed or not) checkbox for that
- after pressing "give up" green square around the pokemon i have guessed. red circle around the ones that user didn't guess
- pokeball icon in exe
- after finishing show all in modal x button
- background music volume in game, after correct guess. toggle on off


# 🧠 Pokémon Quiz  
A fast-paced name guessing quiz for all 9 Pokémon generations and 18 types — built in Unity.

---

## 🎮 About the Game
Think you know every Pokémon by sight?  
Test your knowledge across:

✅ Full National Dex (Gen 1–9)  
✅ Region-only quizzes (Kanto, Johto, Hoenn, etc.)  
✅ Type-based quizzes (Fire, Ghost, Steel, etc.)  
✅ Timer + score tracking  
✅ Tooltip system for revealed Pokémon (name + types)  
✅ Built-in autotype tester for debugging all names  
✅ Clean, minimal UI, keyboard-driven gameplay

---

## 🖼️ Screenshots
<img width="3331" height="1865" alt="image" src="https://github.com/user-attachments/assets/5da3a81a-b868-45b6-9fcb-98c634cb7b06" />
<img width="3331" height="1853" alt="image" src="https://github.com/user-attachments/assets/75af0c1c-6d45-4637-ac5d-99ce3721e113" />
<img width="3333" height="1626" alt="image" src="https://github.com/user-attachments/assets/faba6a2a-4f1c-4346-91f4-78bbb3b1ecb7" />

---

## 🛠️ Tech Stack
| Feature | Details |
|---------|---------|
| Engine | Unity (2022/2023+) |
| Language | C# |
| Build Target | Windows (x64) |
| Rendering | 2D UI, no external frameworks |
| Assets | Pokémon sprites (compressed, optimized) |
| JSON Data | Full Pokédex metadata |

---

## 🚀 Download & Play
**[⬇️ Download latest release](https://www.mediafire.com/file/n9z8r3t0bja8xfd/pkmnquiz_build.zip/file)**  
No install required — unzip & run `PokemonQuiz.exe`

---

## 🎯 Controls
| Action | Key |
|--------|-----|
| Type Pokémon name | Keyboard |
| Submit | `Enter` |
| Backspace | `Backspace` |
| Reveal Types | Button / UI |
| Quit to Menu | `Esc` |

---

## 📦 Build Size Notes
| Format | Size |
|--------|------|
| Raw build | ~550 MB |
| Compressed installer/zip | ~160 MB |

Sprites are texture-compressed at build time using `TextureImporter` automation to reduce size.

---

## 🧩 Development Notes
✅ Sprite compression pipeline (`OptimizeSprites.cs`)  
✅ Tooltip hover system with world-to-UI clamping  
✅ Dynamic test-input autofill tool for debugging names  
✅ ScriptableJSON Pokémon database loaded at runtime  

---

<img width="255" height="409" alt="image" src="https://github.com/user-attachments/assets/61d6c4ac-9ba5-4c29-9a60-bdf425fe887a" />

---

## 📝 License
This project is for educational / fan purposes only.  
**Pokémon and Pokémon images are trademarks of Nintendo / Game Freak / The Pokémon Company.**  
All gameplay code is original and licensed under MIT unless stated otherwise.

---

## 🙌 Credits
| Category | Source |
|----------|--------|
| Pokémon Sprites | Official Pokémon artwork (fair use) |
| Type Icons | Custom vector icons |
| Code & UI | @cheese |
| Inspired by | Sporcle quizzes, Pokémon fandom |

---

## ⭐ Support / Contribute
✅ Star the repo  
✅ Report bugs via Issues  
✅ PRs welcome (UI, features, refactor, etc.)

---
