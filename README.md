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
<img width="3840" height="2158" alt="image" src="https://github.com/user-attachments/assets/44485877-d1f1-4135-814f-4a667ebbc771" />
<img width="3840" height="2160" alt="image" src="https://github.com/user-attachments/assets/220a98aa-eb43-4528-a3be-e8151d99f702" />
<img width="3840" height="2138" alt="image" src="https://github.com/user-attachments/assets/4127a809-77e5-46c6-a997-29ed217fdd5f" />
<img width="3834" height="2135" alt="image" src="https://github.com/user-attachments/assets/bcc7f278-a7af-4de1-bd5c-68cc4705aa66" />




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
**[⬇️ Download latest release](https://github.com/RikuRonka/pkmnquiz/releases/download/latest/pkmnquiz_build1.0.4.zip)**  
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
