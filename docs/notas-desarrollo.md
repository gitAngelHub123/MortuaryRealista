# Mortuary Realista

Mod de gráficos para **The Mortuary Assistant** (Unity 2021.2, HDRP, IL2CPP) que corre sobre **BepInEx 6 IL2CPP**.

## Qué hace

| Parte | Qué cambia | Por defecto |
|---|---|---|
| **Texturas IA** | Sustituye ~200 texturas de 64–1024 px (paredes, suelos, azulejos, hierba, ladrillo, objetos, dientes, ojos…) por versiones escaladas ×4 con IA (modelo 4x-UltraSharp), comprimidas en BC7 (color) y BC5 (relieve). Paredes y suelos llegan a 4096 px; el resto a 2048. Fase 2: piel de los cadáveres y brazos del jugador de 2048 a 4096. | Activado |
| **Realismo HDRP** | Reflejos en pantalla (SSR, el juego los traía apagados), oclusión ambiental de calidad alta, microsombras a partir del relieve. | Activado |
| **DLSS** | El juego trae DLSS pero lo tiene apagado en su pipeline; el plugin puede encenderlo (calidad 2). | **Apagado**: bajo Proton deja bordes punteados y no sube los FPS |
| **Post-proceso** | Exposición +0,35 EV, viñeteo suave 0,15, contraste +5, sin grano. | Activado |
| **Calidad** | Más detalle a distancia (LOD ×2). | Activado |

Opciones probadas y **apagadas** a propósito:
- **Sombras de contacto**: dejan un tramado/moaré sobre las zonas iluminadas por lámparas cercanas.
- **Sombras suaves PCSS** (`FiltroSombras = 2`): bajan de ~70 a ~18 FPS con las 31 luces de la morgue.
- **SSGI** (luz rebotada): opcional, cuesta FPS.

## Opciones dentro del juego (0.5.0)

Las opciones del mod están en la pantalla **OPCIONES** del propio juego (menú principal y pausa), en la pestaña **MOD GRÁFICO**, junto a SETTINGS / CONTROLS (`Interfaz.cs`). Los controles son clones de los del juego (casillas, listas, barras con botón de restablecer y botones con marco), con el mismo marco de selección y fuentes. La página se desplaza con la rueda y tiene barra lateral; la línea sobre CERRAR explica la opción bajo el puntero.
- **Ajustes rápidos**: Original / Equilibrado / Máximo; el que coincide con la configuración actual queda en rojo.
- Secciones: Principal, Avanzado, Cine y color, No disponible. Cada cambio se aplica y se guarda en el `.cfg` (con 0,3 s de espera para las barras).
- Prueba automática: `[Pruebas] CapturasPestañaMod = true` recorre la pestaña en el menú principal y luego en la pausa de la partida y saca capturas (respaldar partidas antes: entra con CONTINUAR).
- El menú F6 (IMGUI) se quitó: sus botones no mostraban la selección.

Arreglos importantes de esta versión:
- **Cuerpos "deshechos en puntitos"**: el compresor BC7 (etcpak) dejaba alfa 254 y los materiales `Ga_Skin_Body` recortan con umbral 1,0. `herramientas/alfa255.py` lo corrige (aplicado a todos los `.mtex` de color e integrado en `procesar.py`).
- **Pieles cruzadas entre cadáveres**: la huella 16×16 no distingue pieles (4,8–5,7). Para texturas con el mismo nombre se usa `texturas_ia/huellas64.tsv` (`herramientas/huella64.py`) con la peor zona 8×8 (`UmbralZona` = 10; correctas ≤ 9, erróneas ≥ 25).
- **Materiales recargados**: el juego recarga materiales desde disco al cambiar de escena (mismo ID, valores originales). El plugin reaplica sus cambios por ID en cada barrido.
- Reflejos: HDRP solo refleja con brillo ≥ 0,9; se baja a 0,4 para que reflejen los suelos.

## Portapapeles legible (`Portapapeles.cs`)

El texto del portapapeles es interfaz (TextMeshPro en un Canvas de mundo) pegada a una hoja **curva**. Medido en el juego (2026-11-24), también con el mod apagado:
- Donde la hoja se curva por delante del texto, el papel lo tapa a medias (banda deslavada arriba). Arreglo: `SeparacionTexto` (0,05 m) acerca el Canvas a la cámara por la línea de vista y lo achica en la misma proporción → en pantalla queda idéntico, pero por delante de la hoja. Con ≥ 0,08 m aparece un panel negro que el juego esconde detrás de la hoja.
- Con **TAA** el texto desaparece al girar la cámara (la interfaz no tiene vectores de movimiento). Arreglo: `SMAAAlLeer` usa SMAA solo mientras se sostiene el portapapeles y restaura TAA al guardarlo.
- `TextoNegro`: el texto no recibe sombras; en gris se veía más claro que el papel sombreado.
- `LuzPortapapeles` / `IntensidadLumenes` (modo 1): el papel brilla un poco (emisión multiplicada por su color). Una luz real deslavaba el texto (la interfaz no recibe luz).
- Iluminación global (SSGI): dejaba puntitos brillantes que parpadean; ningún ajuste del filtro los quita → apagada y fuera del preset Máximo.

Pruebas en vivo sin reiniciar: escribir líneas en `Capturas/comandos.txt` (`Seccion.Clave=valor`, `esperar N`, `captura nombre`, `boton TEXTO`, `portapapeles_dar|tomado|cerrar|quitar|textos`, `girar GRADOS SEGUNDOS`). `herramientas/reiniciar_partida.sh` cierra, instala el DLL, abre, pulsa CONTINUAR y espera la escena.

## Teclas

- **F8**: post-proceso de color sí/no.
- **F9**: relee la configuración sin cerrar el juego.
- **F7**: texturas IA ↔ originales (solo si `ConservarOriginales = true`, usa ~1 GB más de VRAM).
- **F10**: captura del juego en `The Mortuary Assistant/Capturas/`.

Configuración: `BepInEx/config/angel.mortuary.terrorcine.cfg`.

## Archivos

- `Plugin.cs`: volumen de post-proceso, DLSS y calidad.
- `TexturasIA.cs`: carga `indice.tsv` + `.mtex`; reconoce cada textura por nombre, tamaño y una **huella de 16×16 leída de la GPU** (hay texturas distintas con el mismo nombre, como las pieles de cada cadáver); como las texturas del juego no son legibles, crea texturas nuevas y las cambia en los materiales; libera las originales.
- `Realismo.cs`: SSAO, SSR, microsombras, PCSS/SSGI opcionales, FPS en el log cada 10 s.
- `Pruebas.cs`: capturas, pulsar un botón del menú por su texto, pares A/B y prueba de rendimiento (todo apagado por defecto).
- `herramientas/`: pipeline de texturas en Python 3.12 (`venv312`, creado con `uv`).
  - `inventario.py`: lista texturas y materiales del juego.
  - `procesar.py`: extrae, escala con IA, corrige color (retroproyección), genera mips, comprime (BC7/BC5 con etcpak por franjas) → `texturas_ia/*.mtex`. `--fase2` para pieles y brazos.
  - `generar_indice.py`: crea `texturas_ia/indice.tsv`.
  - `verificar.py`, `comparar.py`, `prueba_modelos.py`, `biseccion.sh`: control de calidad y medición.
- `texturas_ia/`: texturas generadas (2,1 GB con la fase 2: 248 entradas en el índice; en juego ~232 reemplazadas, +1,7 GB de VRAM). Enlazada en `BepInEx/plugins/MortuaryRealista/TexturasIA`.

Compilar: `~/.dotnet/dotnet build -c Release` y copiar `bin/Release/net6.0/MortuaryRealista.dll` a `BepInEx/plugins/MortuaryRealista/`.

## Rendimiento

Con dos instancias de Witch Hunt abiertas en la misma GPU (8 GB), las mediciones son ruido: el juego **sin mod** dio entre 26 y 77 FPS en rondas alternadas, y el mod completo entre 30 y 66. No hay diferencia medible separable del ruido. Para medir bien hay que cerrar todo lo demás que use la GPU. Si la VRAM queda justa, bajar `TexturasIA.PresupuestoMB` (1000 → 112 texturas en vez de 232).

## Revertir

- Quitar solo el mod: borrar `BepInEx/plugins/MortuaryRealista/`.
- Quitar BepInEx: borrar los archivos listados en `steamapps/common/BepInEx_archivos_añadidos_TMA.txt` y la opción de lanzamiento `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- Partidas respaldadas en `respaldo_partidas/`.

## Nota

Las texturas IA derivan de las del juego: sirven para uso personal; publicarlas sería redistribuir material con derechos.
