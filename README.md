<div align="center">

# MU World Editor

**Editor visual de mapas para MU Online — compatible con clientes hasta Season 21**

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![Plataforma](https://img.shields.io/badge/plataforma-Windows%20x64-0078D6?logo=windows&logoColor=white)
![OpenGL](https://img.shields.io/badge/OpenGL-2.1%2B-5586A4?logo=opengl&logoColor=white)
![Versión](https://img.shields.io/badge/versión-1.0.0-E6AE50)
![Idiomas](https://img.shields.io/badge/idiomas-ES%20%7C%20EN-lightgrey)

</div>

---

## Descripción

MU World Editor es una herramienta de escritorio para abrir, ver y editar los mapas (`WorldN`) del cliente de MU Online: alturas del terreno, texturas del piso, atributos de celda, transparencia y objetos, con una vista 3D en tiempo real.

Está escrito en **C# / WinForms sobre .NET 9** y dibuja con **OpenGL propio** (WGL + GLSL), sin dependencias externas ni paquetes NuGet.

## Autoría y créditos

| Rol | Persona |
|---|---|
| **Autor original** | **Rodrigo Hauche** — diseño y desarrollo del editor (v1.0) |
| Soporte Season 21, rediseño de interfaz y animaciones | Leonardo |

> El crédito del proyecto original pertenece a **Rodrigo Hauche**. Las modificaciones listadas en el [registro de cambios](#registro-de-cambios) se hicieron sobre su código.

---

## Funciones

### Edición
- **Altura** — subir, bajar, alisar, aplanar o fijar una altura exacta con pincel de tamaño variable.
- **Atributos** — pintar banderas de celda (zona segura, no transitable, etc.) con vista de marcas.
- **Tiles** — pintar las dos capas de textura del piso y su mezcla.
- **Transparencia** — controlar la mezcla entre capas de piso.
- **Objetos** — agregar, mover, rotar, escalar, duplicar y borrar objetos con gizmo 3D, grilla y ajuste.
- **Área** — copiar una región del mapa y pegarla en otro lugar u otro mapa, eligiendo qué se pega (alturas, tiles, atributos, objetos, luz).
- **Reemplazar aspecto** — cambiar las texturas de un objeto por un PNG, crear un cartel plano desde una imagen o apuntar a otro `.bmd`.
- **Luz del terreno** — recalcular la sombra según las alturas y guardarla en `TerrainLight`.
- **Deshacer / rehacer** ilimitado.

### Visualización
- Vista 3D con niebla, cielo en degradado, agua animada y distancia de dibujo ajustable.
- **Animaciones de objetos** — árboles, pájaros, barcos, banderas, etc. se mueven como en el juego (velocidad ajustable).
- Contorno del objeto seleccionado y del que está bajo el mouse.
- Minimapa, ejes de orientación y grilla que se desvanece con la distancia.
- Cuatro modos de sombreado del terreno: luz real, relieve calculado, combinado o plano.
- Exportar el mapa entero visto desde arriba como imagen.

### Formatos soportados

| Archivo | Soporte |
|---|---|
| `.att` (atributos) | Clásico y cifrado Modulus (S21) |
| `.map` (tiles) | Clásico y cifrado Modulus (S21) |
| `.obj` (objetos) | Versiones 0 a 5 |
| `TerrainHeight` `.ozb` | 8 bits y 24 bits (S21) |
| `.bmd` (modelos) | Clásico, cifrado MU y LEA (BMD v15) |
| `.ozj` / `.ozt` (texturas) | Lectura y escritura |

Todos los formatos se guardan **byte a byte igual** a como se leyeron cuando no se modifican.

---

## Requisitos

- Windows 10 (1903 o superior) u 11, **x64**.
- Placa de video con **OpenGL 2.1** o superior.
- Para compilar: **Visual Studio 2022 (17.12+)** o **Visual Studio 2026** con la carga de trabajo **"Desarrollo de escritorio de .NET"** (incluye el SDK de .NET 9).

## Compilar y ejecutar

```bash
git clone https://github.com/<usuario>/MuWorldEditor.git
cd MuWorldEditor
```

1. Abrir `MuWorldEditor.sln` en Visual Studio.
2. Elegir **Debug** para depurar con **F5**, o **Release** para el ejecutable final.
3. **Compilar → Recompilar solución**.

| Acción | Dónde queda |
|---|---|
| Compilar / F5 | `obj\salida\MuWorldEditor.exe` |
| Publicar (exe único, autocontenido) | `realece\MuWorldEditor.exe` |

Desde la línea de comandos:

```bash
dotnet build -c Release
dotnet publish -c Release
```

> Cerrá el editor antes de recompilar: si está abierto, Windows no deja reemplazar el `.exe`.

## Uso rápido

1. Al abrir, la **pantalla de inicio** muestra los mapas recientes.
2. **Elegir carpeta del cliente** → seleccionar la carpeta `Data` del cliente de MU.
3. Elegir el mapa en la lista de la barra superior.
4. Editar con las herramientas (teclas `1`–`7`) y guardar con `Ctrl+S`.

También se puede abrir una carpeta `WorldN` suelta, fuera del cliente.

## Atajos de teclado

Presioná **F1** dentro del editor para ver la lista completa.

| Grupo | Tecla | Acción |
|---|---|---|
| Archivo | `Ctrl+S` / `Ctrl+Z` / `Ctrl+Y` | Guardar / deshacer / rehacer |
| Herramientas | `1` … `7` | Ver, Altura, Atributos, Tiles, Transparencia, Objetos, Área |
| | `Tab` | Mostrar u ocultar el panel |
| Cámara | Clic derecho | Girar |
| | Clic del medio / `Espacio` | Desplazar |
| | Rueda | Acercar y alejar |
| | `W A S D` / flechas | Mover la vista |
| | `F` / `Inicio` | Encuadrar selección / volver al inicio |
| Pincel | `Ctrl` + rueda | Tamaño del pincel |
| | `Shift` + pincel | Efecto inverso |
| Objetos | `M` / `R` / `Y` | Mover / rotar / escalar |
| | `Ctrl+C` / `Ctrl+V` / `Ctrl+D` | Copiar / pegar / duplicar |
| | `Supr` | Borrar |
| | `Ctrl` + clic | Objeto nuevo |
| | `RePág` / `AvPág` | Elegir objeto apilado |
| Vista | `G` / `T` | Grilla de celdas / marcas de atributo |

## Opciones de línea de comandos

```text
MuWorldEditor.exe --selftest [rutaData]
MuWorldEditor.exe --render <rutaData> <mundo> <salida.png> [att] [grid] [dist=3500]
MuWorldEditor.exe --edittest <rutaData> <mundo> [salida.txt]
```

---

## Estructura del proyecto

```text
MuWorldEditor/
├── App/        Ventana principal, vista 3D, tema, controles, pantalla de inicio
├── Editing/    Estado del editor, historial, copiar/pegar áreas
├── Formats/    Lectura/escritura de BMD, MAP, ATT, OBJ, OZB, texturas y cifrados
├── Render/     OpenGL (WGL), shaders, cámara, terreno, modelos, gizmo
├── World/      Mundo cargado, catálogo de objetos y tiles, luz, exportación
├── Lang/       Traducciones (en.txt; el español es el idioma base)
├── ObjectList/ Nombres de objetos por mapa (CSV embebido)
└── Program.cs  Arranque, pantalla de carga y registro de inicio
```

## Solución de problemas

- **El programa se cierra al iniciar**: cada arranque escribe `inicio.log` al lado del `.exe`. Si un arranque falla, el siguiente entra en **modo seguro** (sin suavizado MSAA y con la barra de título de Windows) y guarda el registro fallido en `inicio.anterior.log`.
- **Se ve igual que antes después de cambiar el código**: el build falló o el editor estaba abierto; revisá la Lista de errores de Visual Studio.
- **Opciones visuales** (suavizado, barra propia, cielo, minimapa, sombreado): pestaña **Ver → Presentación**. Algunas se aplican al reiniciar.

---

## Registro de cambios

### Soporte Season 21
- Lectura y escritura de `.att` y `.map` con cifrado **Modulus** (8 cifrados de bloque).
- Modelos **BMD v15** con cifrado **LEA**.
- Archivos `.obj` versiones 1 a 5 (bytes extra por objeto preservados).
- Alturas `TerrainHeight` de **24 bits** (`.ozb` S21).
- Verificado con ida y vuelta byte a byte sobre mapas reales de un cliente S21.

### Rediseño de la interfaz
- Paleta oscura con acento dorado, tipografía moderna y set de íconos vectoriales propios.
- Pantalla de carga y **pantalla de inicio** con mapas recientes y miniaturas.
- Barra de título propia (opcional), barra de herramientas con íconos agrupados y barra de estado segmentada.
- Pestañas tipo segmentos, secciones plegables, botones, casillas, campos numéricos y deslizadores dibujados a mano.
- Barras de desplazamiento y menús nativos en modo oscuro.
- Lista de objetos enriquecida con buscador.
- Notificaciones emergentes, diálogos con el mismo estilo y ventana de atajos (`F1`); atajos en los tooltips.
- La ventana recuerda tamaño, posición, pestaña y secciones plegadas.

### Vista 3D
- Cielo en degradado, niebla ligada al cielo y brillo en el agua.
- Nueva iluminación de modelos y cuatro modos de sombreado del terreno.
- Contorno de selección y de hover, grilla que se desvanece, cursor de pincel 3D.
- Gizmo con manijas de plano (XY, XZ, YZ).
- Minimapa, ejes de orientación y transiciones suaves de cámara.
- Suavizado de bordes **MSAA** (opcional).
- **Animaciones de objetos** con interpolación de huesos y control de velocidad.

### Correcciones
- El build ya no falla si faltan `AVISO.txt` / `LEEME.txt`; se agregó la configuración Debug.
- Corregido un cierre al iniciar con MSAA (error 1410, "Esta clase ya existe").
- Registro de arranque (`inicio.log`) y modo seguro automático.
- Traducciones al inglés de todos los textos nuevos.

---

## Licencia

```text
(c) 2026 Rodrigo Hauche - Todos los derechos reservados.
Uso privado, prohibida su redistribucion.
```

Este proyecto **no** tiene licencia de código abierto. Todos los derechos pertenecen a su autor original, Rodrigo Hauche. Consultá con él antes de copiar, redistribuir o publicar el código o los binarios.

*MU Online es una marca de Webzen Inc. Este proyecto no está afiliado ni respaldado por Webzen.*
