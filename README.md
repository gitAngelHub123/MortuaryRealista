# Mortuary Realista

A free graphics mod for **The Mortuary Assistant** (Unity HDRP, IL2CPP) that runs on **BepInEx 6**.
It improves lighting, reflections, shadows, materials and color, and adds a **GRAPHICS MOD** tab inside the game's own **Options** menu so you can tune everything in-game.

*Versión en español más abajo.*

## Features

- **GRAPHICS MOD tab in the game's Options menu** (main menu and pause menu), built with the game's own checkboxes, dropdowns and sliders. Each option shows a short description when you hover over it. Changes apply instantly and are saved.
- **Quick presets:** Original / Balanced / Maximum. The preset matching your current settings is highlighted.
- **Lighting:** screen-space reflections (off in the base game), high-quality ambient occlusion, micro shadows, realistic indoor bounce light, higher-resolution shadows, optional volumetric fog with light shafts.
- **Materials:** stronger surface relief on walls, floors, stone and wood; per-material shine (tiles, glass, metal, fabric); finer skin detail and subsurface scattering; denser grass.
- **Cinematic & color:** tone mapping (Neutral / ACES), exposure, contrast, saturation, vignette, optional film grain, chromatic aberration, lens distortion, motion blur and depth of field.
- **Readable clipboard:** the clipboard text no longer fades in curved areas or disappears when you move the camera, and the paper glows slightly so you can read it in the dark.
- **Level of detail and draw distance** multipliers, 16x anisotropic filtering.
- Menu language follows the game: Spanish if the game is in Spanish, English otherwise.

The mod **does not modify any game files** on disk; everything happens at runtime.

### Optional AI texture pack

The plugin can load higher-resolution versions of the game's textures (upscaled with AI). That pack is **not included** in this repository because it is derived from the game's original textures, which belong to DarkStone Digital. The `herramientas/` folder contains the pipeline used to build it.

## Requirements

- The Mortuary Assistant (Steam, PC).
- **BepInEx 6 for Unity IL2CPP x64** (bleeding-edge build, tested with `6.0.0-be.793`): <https://builds.bepinex.dev/projects/bepinex_be>

## Installation

1. Install BepInEx 6 IL2CPP into the game folder and start the game once so BepInEx generates its files, then close it.
2. Copy the `BepInEx` folder from the release zip into the game folder (the plugin goes to `BepInEx/plugins/MortuaryRealista/`).
3. Start the game and open **Options → GRAPHICS MOD**.

**Linux / Steam Deck (Proton):** add this to the game's Steam launch options: `WINEDLLOVERRIDES="winhttp=n,b" %command%`

### Keys

- **F8:** color effects on/off.
- **F9:** reload the configuration file.
- **F10:** save a screenshot in the game folder (`Capturas/`).

Configuration file: `BepInEx/config/angel.mortuary.terrorcine.cfg`.

### Uninstall

Delete `BepInEx/plugins/MortuaryRealista/`.

## Building

```
dotnet build -c Release
```
The project references the game's BepInEx interop assemblies (see `MortuaryRealista.csproj`, `GameDir`).

## Credits

- **The Mortuary Assistant** and all of its assets © DarkStone Digital. This is an unofficial fan mod.
- [BepInEx](https://github.com/BepInEx/BepInEx) and Il2CppInterop.
- Texture pipeline: [4x-UltraSharp](https://openmodeldb.info/models/4x-UltraSharp) model by Kim2091 (CC BY-NC-SA 4.0, not included), [spandrel](https://github.com/chaiNNer-org/spandrel), [UnityPy](https://github.com/K0lb3/UnityPy), [etcpak](https://github.com/K0lb3/etcpak).

## License

MIT © 2026 Angel — covers the source code only. See [LICENSE](LICENSE).

---

# Mortuary Realista (español)

Mod gráfico gratuito para **The Mortuary Assistant** (Unity HDRP, IL2CPP) que corre sobre **BepInEx 6**.
Mejora la iluminación, los reflejos, las sombras, los materiales y el color, y agrega la pestaña **MOD GRÁFICO** dentro del menú **Opciones** del propio juego para ajustarlo todo sin salir de él.

## Qué trae

- **Pestaña MOD GRÁFICO en Opciones** (menú principal y pausa), hecha con las mismas casillas, listas y barras del juego. Cada opción muestra una descripción al pasar el puntero. Los cambios se aplican al instante y se guardan.
- **Ajustes rápidos:** Original / Equilibrado / Máximo; el que coincide con tu configuración queda marcado.
- **Iluminación:** reflejos en pantalla (el juego los traía apagados), oclusión ambiental de calidad alta, microsombras, luz interior realista, sombras de mayor resolución y niebla volumétrica opcional con haces de luz.
- **Materiales:** más relieve en paredes, suelos, piedra y madera; brillo por material (azulejos, vidrio, metal, tela); más detalle y dispersión en la piel; pasto más tupido.
- **Cine y color:** mapeo tonal (Neutral / ACES), exposición, contraste, saturación, viñeteo y, opcionales, grano, aberración cromática, distorsión de lente, desenfoque de movimiento y profundidad de campo.
- **Portapapeles legible:** el texto ya no se deslava en las zonas curvas ni desaparece al mover la cámara, y el papel brilla un poco para leerlo a oscuras.
- **Nivel de detalle y distancia de dibujado** ajustables, filtrado anisotrópico 16x.
- El menú sale en el idioma del juego: español si el juego está en español; inglés en los demás.

El mod **no modifica ningún archivo del juego** en el disco: todo ocurre mientras el juego corre.

### Pack opcional de texturas IA

El plugin puede cargar versiones de mayor resolución de las texturas del juego (escaladas con IA). Ese pack **no está incluido** en este repositorio porque deriva de las texturas originales del juego, que son de DarkStone Digital. La carpeta `herramientas/` contiene el proceso con el que se genera.

## Requisitos

- The Mortuary Assistant (Steam, PC).
- **BepInEx 6 para Unity IL2CPP x64** (versión bleeding edge, probado con `6.0.0-be.793`): <https://builds.bepinex.dev/projects/bepinex_be>

## Instalación

1. Instala BepInEx 6 IL2CPP en la carpeta del juego, ábrelo una vez para que BepInEx genere sus archivos y ciérralo.
2. Copia la carpeta `BepInEx` del zip en la carpeta del juego (el plugin queda en `BepInEx/plugins/MortuaryRealista/`).
3. Abre el juego y entra a **Opciones → MOD GRÁFICO**.

**Linux / Steam Deck (Proton):** agrega en las opciones de lanzamiento de Steam: `WINEDLLOVERRIDES="winhttp=n,b" %command%`

### Teclas

- **F8:** efectos de color sí/no.
- **F9:** recarga la configuración.
- **F10:** captura de pantalla en la carpeta del juego (`Capturas/`).

Configuración: `BepInEx/config/angel.mortuary.terrorcine.cfg`.

### Desinstalar

Borra `BepInEx/plugins/MortuaryRealista/`.

## Créditos

- **The Mortuary Assistant** y todo su contenido © DarkStone Digital. Este es un mod no oficial hecho por un fan.
- [BepInEx](https://github.com/BepInEx/BepInEx) e Il2CppInterop.
- Proceso de texturas: modelo [4x-UltraSharp](https://openmodeldb.info/models/4x-UltraSharp) de Kim2091 (CC BY-NC-SA 4.0, no incluido), [spandrel](https://github.com/chaiNNer-org/spandrel), [UnityPy](https://github.com/K0lb3/UnityPy), [etcpak](https://github.com/K0lb3/etcpak).

## Licencia

MIT © 2026 Angel — cubre solo el código. Ver [LICENSE](LICENSE).
