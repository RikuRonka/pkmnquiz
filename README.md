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
<img width="2559" height="1439" alt="image" src="https://github.com/user-attachments/assets/70f067d4-7a16-469f-9a21-f023687b7dbe" />
<img width="2559" height="1439" alt="image" src="https://github.com/user-attachments/assets/713399b0-7248-4d0c-8e9a-4ad298de33d7" />
<img width="2559" height="1439" alt="image" src="https://github.com/user-attachments/assets/fac085cd-380e-4d11-8a8e-fbe089c9faee" />
<img width="2559" height="1438" alt="image" src="https://github.com/user-attachments/assets/73e6d03b-62e7-44b0-842d-45d1e860a054" />


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
