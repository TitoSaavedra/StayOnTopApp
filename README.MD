# StayOnTop - Mini 📌

**Utilidad ultra-ligera de interoperabilidad Win32 para la gestión de capas y transparencia de ventanas.**

Diseñado para usuarios que necesitan anclar ventanas específicas sobre aplicaciones en pantalla completa o juegos. Estética **Retro Windows 95** potenciada con **C# / WPF** y **WebView2**.

---

## 🚀 Características Principales

- **Anclaje de Ventanas (Always on Top):** Mantiene cualquier proceso o manejador de ventana (HWND) específico por encima de todos los demás.
- **Modo Fantasma (Click-Through):** Hace que las ventanas ancladas sean transparentes a los clics del ratón, permitiendo interactuar con el fondo sin interferencias.
- **Selector de Precisión:** Herramienta "Pick with mouse" para capturar ventanas específicas directamente desde la pantalla.
- **Modo Eco-Performance:** Escaneo inteligente de procesos que reduce la carga de CPU automáticamente cuando hay ventanas ancladas.
- **Integración con la Bandeja del Sistema:** Minimiza la app al _System Tray_ para mantener la barra de tareas limpia mientras trabajas en segundo plano.
- **Interfaz Minimalista:** UI nostálgica de alto rendimiento construida con Tailwind CSS y Lucide Icons.

---

## 🛠️ Stack Tecnológico

| Capa             | Tecnología           | Propósito                                              |
| :--------------- | :------------------- | :----------------------------------------------------- |
| **Backend**      | .NET 10 / C#         | Lógica central y gestión del sistema.                  |
| **Frontend**     | HTML5 / Tailwind CSS | Renderizado de UI mediante Microsoft Edge WebView2.    |
| **Interop**      | Win32 API (P/Invoke) | Manipulación de ventanas de bajo nivel (`User32.dll`). |
| **Arquitectura** | Event-Driven         | Comunicación en tiempo real entre la UI y el SO.       |

---

## 📥 Instalación

1.  Descarga el ejecutable `StayOnTopApp.exe` desde la sección de **Releases**.
2.  Asegúrate de tener instalado el **WebView2 Runtime** (incluido por defecto en Windows 10/11 actualizado).
3.  Ejecuta la aplicación. No requiere instalación (**Modo Portable**).

> **Nota:** Para aplicaciones protegidas (como ciertos juegos con Anti-Cheat), es posible que necesites ejecutar `StayOnTop` como **Administrador** para permitir la manipulación de ventanas externas.

---

## 🎮 Guía de Uso

1.  **Selección:** Elige un proceso de la lista desplegable o haz clic en **"Pick window with mouse"**.
2.  **Captura:** Si usas el selector, coloca el cursor sobre la ventana deseada. Presiona **ESC** para cancelar o **Click Izquierdo** para seleccionar.
3.  **Anclaje:** Haz clic en **PIN TO TOP**. La ventana flotará y permitirá que los clics pasen a través de ella.
4.  **Gestión:** Usa la lista de **Pinned Items** para rastrear y eliminar anclajes activos.

---

## 🏗️ Compilación y Desarrollo

Si deseas compilar el proyecto desde el código fuente:

1.  Clona el repositorio: `git clone https://github.com/TitoSaavedra/StayOnTopApp.git`
2.  Abre la solución en **Visual Studio 2022**.
3.  Establece la configuración de compilación en **Release**.
4.  Publica como **Single File**:
    - **Target Runtime:** `win-x64`
    - **Deployment Mode:** `Self-contained`
    - **Opciones:** `Produce single file`, `Trim unused code`.

---

## 📜 Licencia

Este proyecto está bajo la licencia **MIT**. Siéntete libre de usarlo, modificarlo y distribuirlo.

---

**Desarrollado con 💻 y ☕ por [TitoSaavedra](https://github.com/TitoSaavedra)** _"Optimizando cosas que nadie pidió optimizar."_
