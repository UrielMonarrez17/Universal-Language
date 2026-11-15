# Menú de la demo — Alterlife VR

Abrir `Assets/Scenes/MainMenu.unity` y pulsar **Play**. La escena se agrega al inicio de Build Settings, conservando `SampleScene`. El menú es un Canvas en World Space y también está disponible como prefab en `Assets/Alterlife/Menu/MainMenu.prefab`.

## Controles

- Quest: apuntar con cualquiera de los controles y presionar el gatillo para activar las flechas, el marcador de Francia o un escenario. Mantener presionado y arrastrar sobre el globo para cambiar de orientación.
- Escritorio: clic y arrastre con el ratón. Escape o **‹ World** vuelve al globo.
- Francia muestra el progreso al apuntar al marcador y abre el libro al seleccionarla.
- Casa, café y aduana corresponden a dificultades de 1, 3 y 5 estrellas. Al elegirlas se conserva la selección y se resalta la ilustración.

## Alcance

Se conserva el arte y los textos del Figma. El globo recorre sus tres vistas ilustradas, con cambio por pasos; no es una esfera 3D con rotación libre. Francia es el único destino seleccionable. La selección termina en el menú: no implementa aún las actividades ni carga escenas de casa/café/aduana.

`MainMenuController.SelectedCountry`, `SelectedScenario` y el evento `ScenarioSelected` son el punto de conexión para las actividades posteriores. El índice del evento es 0=casa, 1=café, 2=aduana. El progreso 0% es el estado inicial de la demo, sin persistencia.

Se reutilizan el XR Origin y las acciones de los Starter Assets. La locomoción está deshabilitada solo en la instancia del menú. El tamaño del Canvas es 1440×1024, a escala 0.0016 y a 2 metros del origen, sin mover la cámara del usuario. El prefab necesita la cámara del XR Origin asignada en el Canvas si se usa en otra escena.

## Diseño y recursos

Fuente: https://www.figma.com/design/PYyMcxZCLBrb6vIM5RGhGS/Final

| Recurso local (Art/) | Nodo / uso |
| --- | --- |
| GlobeAtlas.png | 45:177, 36:18, 49:286; imagen original con recortes UV del diseño |
| CountryPins.png | 49:209; marcador de Francia, recorte UV original |
| ArrowLeft.png, ArrowRight.png | 45:180 y 45:184; exportación raster de los vectores originales |
| Room.png | 49:352; fondo del libro |
| Book.png | 55:354; libro |
| Apartment.png | 55:355; casa |
| Cafe.png | 55:356; café |
| Customs.png | 55:357; aduana |
| StarFilled.png, StarEmpty.png | 55:365 y 55:366; estrellas originales |

Se usan archivos locales, sin depender de enlaces temporales de Figma. Las imágenes conservan su proporción con el recorte `object-cover` del diseño. Texto editable con la fuente Inter ya incluida en el proyecto. El botón de regreso y la indicación de selección se añaden al pie para cerrar la navegación de la demo.

## Validación reproducible

`Alterlife > Validate Main Menu` ejecuta comprobaciones en Play Mode: navegación, arrastre, bloqueo de Francia cuando está oculta, callbacks de dificultad, regreso, referencias de imágenes y raycasts de UI. Produce capturas reales de Unity en `Artifacts/Menu/`.

El generador `Alterlife > Create Main Menu Scene` sirve para una creación inicial y se niega a sobrescribir una escena existente. Los elementos creados se pueden editar normalmente en la jerarquía y el Inspector.

La prueba con un visor Quest físico es necesaria para confirmar la comodidad, el seguimiento y los gatillos en el dispositivo; las pruebas automáticas de escritorio no la sustituyen.

Resultado de esta implementación: compilación en Unity 6000.5.9f1 y pruebas de Play Mode aprobadas. Se verificaron raycasts tanto de ratón como de `TrackedDeviceGraphicRaycaster`, las tres orientaciones, el arrastre, la selección de Francia y escenarios, el regreso y las referencias persistentes. Las capturas se revisaron visualmente contra Figma. No se generó APK ni se probó con un visor físico.
