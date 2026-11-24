#!/bin/bash
# Cierra el juego, instala el DLL compilado, abre el juego, pulsa CONTINUAR y espera la escena Mortuary.
G="$HOME/.local/share/Steam/steamapps/common/The Mortuary Assistant"; L="$G/BepInEx/LogOutput.log"
ps -eo pid,args | awk '/The Mortuary Assistant.exe *$/ && !/awk|bash/ {print $1}' | xargs -r kill
for i in $(seq 1 25); do ps -eo args | grep -q "[T]he Mortuary Assistant.exe *$" || break; sleep 1; done
sleep 6
cp "$HOME/Proyectos/MortuaryRealista/bin/Release/net6.0/MortuaryRealista.dll" "$G/BepInEx/plugins/MortuaryRealista/"
rm -f "$G/Capturas/comandos.txt"
(steam steam://rungameid/1295920 >/dev/null 2>&1 &)
sleep 12
for i in $(seq 1 150); do grep -q "Comando: botón CONTINUAR" "$L" 2>/dev/null && break; printf 'boton CONTINUAR\n' > "$G/Capturas/comandos.txt"; sleep 2; done
for i in $(seq 1 150); do grep -q "Escena: Mortuary" "$L" && break; sleep 1; done
sleep "${1:-20}"; echo "en partida"
