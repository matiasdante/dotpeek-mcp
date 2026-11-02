# dotpeek-mcp

Servidor MCP (stdio) que deja a un agente descompilar assemblies .NET con **el mismo motor de dotPeek**: el lector de metadata de JetBrains y el renderer de C# de `JetBrains.Decompiler.Core`, con el mismo pipeline que usa dotPeek para mostrar un tipo. No abre la GUI.

Contexto, análisis y autorización: [research/dotpeek-mcp](../research/dotpeek-mcp/README.md).

## Requisitos

- Windows x64 con dotPeek 2026.2 instalado (por defecto en `%LOCALAPPDATA%\JetBrains\Installations\dotPeek262`).
- .NET SDK para compilar (el binario es .NET Framework 4.7.2).

Las DLL de JetBrains no se copian ni se redistribuyen: se compila contra la instalación local y se cargan desde ahí en tiempo de ejecución.

## Compilar y registrar

```powershell
dotnet build -c Release
claude mcp add dotpeek -- "D:\development\dotpeek-mcp\bin\Release\net472\DotPeekMcp.exe"
```

Si dotPeek está en otra carpeta: `dotnet build -c Release -p:DotPeekHome=<carpeta>` y la variable `DOTPEEK_HOME=<carpeta>` al registrar (`claude mcp add dotpeek --env DOTPEEK_HOME=<carpeta> -- ...`).

## Tools

| Tool | Argumentos | Devuelve |
|---|---|---|
| `load_assembly` | `path` | nombre, versión, target framework, referencias, cantidad de tipos |
| `list_types` | `path`, `filter` opcional | `kind visibilidad Nombre.Completo` por línea |
| `get_type_members` | `path`, `type` | métodos, propiedades, campos y eventos con firma |
| `decompile_type` | `path`, `type` | C# del tipo |
| `decompile_method` | `path`, `type`, `method` | C# de todas las sobrecargas con ese nombre |
| `search_members` | `path`, `query` | tipos y miembros cuyo nombre contiene el texto (máx. 200) |
| `open_in_dotpeek` | `path` | abre el assembly en la GUI de dotPeek (reusa la instancia abierta) |

Los tipos anidados usan `+` (`Externo+Interno`).

## Configuración

| Variable | Default | Uso |
|---|---|---|
| `DOTPEEK_HOME` | `%LOCALAPPDATA%\JetBrains\Installations\dotPeek262` | carpeta de dotPeek |
| `DOTPEEK_MCP_ALLOWED_DIRS` | sin límite | carpetas permitidas separadas por `;`; fuera de ellas la tool falla |
| `DOTPEEK_MCP_MAX_CHARS` | `60000` | corta respuestas largas con un aviso |

## Verificación

```powershell
python tests/smoke_test.py
```

Compila el servidor y un fixture .NET 8, y recorre las tools sobre el fixture y sobre el propio `DotPeekMcp.exe` (.NET Framework). También cubre errores (tipo y archivo inexistentes) y el límite de rutas.

## Límites conocidos

- Solo Windows y requiere dotPeek instalado; una actualización mayor de dotPeek puede romper la API interna usada (fijada a 2026.2.4).
- Sin XML doc ni resolución vía PSI: la salida puede diferir en detalles de la de la GUI (nombres de parámetros de librerías sin PDB, anotaciones).
- Los assemblies cargados quedan en memoria mientras vive el proceso.
