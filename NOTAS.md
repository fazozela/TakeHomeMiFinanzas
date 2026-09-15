# Game of Drones — Notas de estudio

Bitácora del desarrollo: qué hicimos en cada paso y **por qué**.
Stack: .NET 8 (minimal APIs) + EF Core + SQL Server + Angular 21, todo en Docker.

---

## Paso 0 — Leer el enunciado

El juego (piedra/papel/tijera, primero a 3) es trivial. Lo que realmente se evalúa:

1. **Reglas de movimientos editables en runtime.** Se pueden agregar movimientos nuevos
   (`dog kills paper`) y cambiar quién mata a quién sin recompilar.
2. **Empates reales.** Con reglas custom, dos movimientos pueden no matarse entre sí
   (Rock vs Dog). La ronda queda sin ganador y el juego sigue.
3. **Persistencia**: cuántas partidas ganó cada jugador.
4. Requisitos explícitos del PDF: sin connection strings hardcodeadas, sin código muerto,
   sin carpetas de paquetes en el repo, README con instrucciones.

**La decisión de diseño central:** las reglas van en una tabla, no en código.
Un `switch` o un `enum` de movimientos hace imposible el punto 1. La mayoría de la gente
resuelve esto con `if (a == Rock && b == Scissors)` y ahí pierde el ejercicio.

---

## Paso 1 — Estructura del proyecto

### Un solo repositorio

```
TakeHomeMiFinanzas/
├── docker-compose.yml     ← orquesta db + api + web
├── README.md
├── .env                   ← password (NO se commitea)
├── .env.example           ← plantilla (sí se commitea)
├── GameOfDrones.sln       ← solo el backend, para abrir en Rider
├── backend/
└── frontend/
```

**Por qué un repo y no dos:** el PDF pide un link al repo y un README con instrucciones.
El evaluador tiene que hacer *un clone y un comando*. Dos repos obligan a explicar
cómo se conectan entre sí.

**Por qué el `.sln` apunta solo al backend:** abrís la solución en Rider y tenés el
backend limpio, sin que el IDE intente indexar `node_modules`.

### Comandos

```bash
git init
dotnet new gitignore
dotnet new sln -n GameOfDrones
dotnet new webapi -n GameOfDrones.Api -o backend --framework net8.0
dotnet sln add backend/GameOfDrones.Api.csproj
```

### Estructura interna del backend: plana

`Models/`, `Data/`, `Endpoints/` dentro de **un solo proyecto**.

No usamos Clean Architecture (`.Domain` / `.Application` / `.Infrastructure` / `.Api`).
Para 5 entidades son cuatro proyectos casi vacíos que el evaluador tiene que navegar.
Un proyecto bien ordenado se lee mejor que cuatro ceremoniales.

### .gitignore

`dotnet new gitignore` cubre `bin/` y `obj/`, **pero no** `node_modules/`. Agregar a mano:

```
node_modules/
frontend/dist/
frontend/.angular/
.env
```

Hacerlo **antes** del primer `npm install`. Sacar `node_modules` del historial de git
una vez que entró es un dolor evitable.

---

## Paso 2 — Paquetes NuGet (y los problemas que dieron)

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version '8.0.*'
dotnet add package Microsoft.EntityFrameworkCore.Design --version '8.0.*'
```

### Problema 1: `NU1202 — package 10.0.12 is not compatible with net8.0`

`dotnet add package` sin `--version` agarra **la última**, que hoy es para .NET 10.
Hay que pinear a `8.0.*`.

### Problema 2: `zsh: no matches found: 8.0.*`

zsh intenta expandir el `*` como glob de archivos. Solución: **comillas simples**
alrededor de la versión.

### Problema 3: el tool `dotnet-ef`

Mismo conflicto de versiones. Lo instalamos **local al repo** en vez de global:

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef --version '8.0.*'
```

Ventajas sobre el global: no pisa la versión que quizás usás en otros proyectos, y deja
`.config/dotnet-tools.json` versionado — quien clone corre `dotnet tool restore` y tiene
exactamente tu versión.

### Paquetes que ya venían de la plantilla

- **Swashbuckle.AspNetCore** — genera Swagger UI (`/swagger`). Nos sirve para probar
  el backend antes de que exista el frontend.
- **Microsoft.AspNetCore.OpenApi** — agrega metadatos OpenAPI a los minimal APIs.
  Describe los endpoints; Swashbuckle los dibuja.

**Regla:** nunca tocar el botón "Update" de NuGet en este proyecto. Todo lo que ofrece
es versión 10 y devuelve el `NU1202`.

---

## Paso 3 — Contenedores

Por ahora solo `db` + `api`. El frontend se agrega cuando exista.

### `backend/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GameOfDrones.Api.dll"]
```

**Multi-stage:** compilás con el SDK (~800MB) y corrés con el runtime (~200MB).
La imagen final no lleva compilador.

**`COPY *.csproj` antes del resto:** Docker cachea por capa. Mientras no cambien las
dependencias, el `restore` no se vuelve a ejecutar en cada build.

### `docker-compose.yml`

```yaml
services:
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    platform: linux/amd64
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${SA_PASSWORD}
    ports: ["1433:1433"]
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -C -Q "SELECT 1"
      interval: 10s
      retries: 10
      start_period: 30s

  api:
    build: ./backend
    environment:
      ConnectionStrings__Default: "Server=db;Database=GameOfDrones;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True"
      ASPNETCORE_ENVIRONMENT: Development
    ports: ["5080:8080"]
    depends_on:
      db:
        condition: service_healthy
```

Los cinco detalles que importan:

1. **`platform: linux/amd64`** — SQL Server no tiene imagen nativa ARM. En Mac con chip
   M-series corre emulado bajo Rosetta. Sin esta línea, Docker no encuentra imagen.

2. **Healthcheck + `condition: service_healthy`** — SQL Server tarda ~30s en aceptar
   conexiones. Sin esto la API arranca primero, no conecta y se muere. Sub-detalles:
   - `mssql-tools18`: la imagen 2022 usa la versión 18 de las tools.
   - `-C`: confía en el certificado autofirmado.
   - `$$`: escapa el `$` para que lo expanda el shell del contenedor, no Compose.

3. **`ConnectionStrings__Default`** — el doble guión bajo es cómo .NET mapea variables
   de entorno a configuración anidada. En el código se lee con
   `builder.Configuration.GetConnectionString("Default")`.
   Esto **es** el requisito del PDF *"avoid hard-coded connection strings"*:
   ninguna password aparece en el repo.

4. **`5080:8080`** — dos razones:
   - La imagen `aspnet:8.0` escucha en **8080** por defecto (cambió en .NET 8; antes era 80).
   - El **5000 en macOS está ocupado por AirPlay Receiver** (proceso `ControlCenter`).
     No conviene matarlo: `launchd` lo revive. Y el evaluador puede estar en Mac y
     chocar con lo mismo.

5. **`TrustServerCertificate=True`** en la connection string — SQL Server usa un cert
   autofirmado; sin esto EF rechaza la conexión.

### `.env` (en `.gitignore`) y `.env.example` (commiteado)

```
SA_PASSWORD=Str0ng!Passw0rd
```

**La password de SA tiene política:** mínimo 8 caracteres y al menos 3 de estas 4
categorías: mayúscula, minúscula, dígito, símbolo. `Str0ng!Passw0rd` sola tiene 2
y el contenedor arranca y muere con `Password validation failed`.

### `Program.cs`

```csharp
using GameOfDrones.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", async (AppDbContext db) => await db.Database.CanConnectAsync());

app.Run();
```

Qué sacamos de la plantilla y por qué:

- **`app.UseHttpsRedirection()`** — dentro del contenedor solo escuchamos HTTP en 8080.
  Con esa línea te redirige a un puerto HTTPS que no existe. TLS va en el reverse proxy.
- **El `if (app.Environment.IsDevelopment())` alrededor de Swagger** — queremos que el
  evaluador abra `/swagger` sin configurar nada.
- **Todo el `WeatherForecast`** — el PDF pide explícitamente que no haya código sin usar.

**Por qué el endpoint `/health`:** registrar el `DbContext` no prueba nada; EF no abre la
conexión hasta que alguien la usa. Este endpoint sí la abre. Y no es código descartable:
sirve para el resto del proyecto y queda bien documentado en el README.

### Comandos

```bash
docker compose up --build    # tocaste código del backend
docker compose up            # solo reiniciar
docker compose down          # bajar
```

`--build` es necesario porque Compose reusa la imagen cacheada y **no se entera** de que
cambiaste el código. Sin `-d` durante el desarrollo: querés ver los logs en vivo, ahí
aparecen los errores de conexión.

### Resultado: `/health` devolvió `false`

Log de la db:

```
Login failed for user 'sa'. Reason: Failed to open the explicitly specified database 'GameOfDrones'
```

Esto es **lo esperado** en este punto: la password es correcta, la red entre contenedores
funciona, SQL Server responde. Solo falta que la base exista — eso lo hacen las migraciones,
y para tener una migración hace falta tener entidades.

---

## Paso 4 — El modelo de datos

```
Move       Id, Name
MoveRule   WinnerMoveId, LoserMoveId        ← clave compuesta, sin Id propio
Player     Id, Name
Game       Id, Player1Id, Player2Id, WinnerId?, CreatedAt
Round      Id, GameId, Number, Move1Id, Move2Id, WinnerId?
```

### Por qué así

**Las reglas viven en `MoveRule`, no en código.** Resolver una ronda es
`SELECT 1 FROM MoveRules WHERE WinnerMoveId=@a AND LoserMoveId=@b`.
Cero `if/else`, cero `switch`, cero enums. Agregar "Dog" es un `INSERT`.
Esto es lo que hace posible el requisito de cambiar las reglas en runtime.

**`MoveRule` no lleva `Id` propio.** La clave es el par `(WinnerMoveId, LoserMoveId)`:
que "papel mata piedra" exista dos veces no tiene sentido, y la clave compuesta lo
impide a nivel de base. Hay que declararla a mano:
`HasKey(r => new { r.WinnerMoveId, r.LoserMoveId })` — EF no infiere claves compuestas.

**Los `?` son estados reales del juego, no casos borde.**
- `Game.WinnerId` null = partida en curso.
- `Round.WinnerId` null = empate. Con reglas custom el empate deja de ser solo
  "mismo movimiento": Rock vs Dog no se matan entre sí. El PDF lo dice explícito
  ("nothing happens and the game continues").

**No hay columna `Player.GamesWon`.** Sale de `COUNT(Games WHERE WinnerId = p.Id)`.
Un dato derivado que guardás es un dato que se desincroniza.

**Índices únicos en `Move.Name` y `Player.Name`.** El de `Player` es el que hace que
las estadísticas funcionen: "Fazo" es siempre el mismo jugador entre partidas.

### Configuración en `OnModelCreating`

```csharp
b.Entity<Move>().HasIndex(m => m.Name).IsUnique();
b.Entity<Player>().HasIndex(p => p.Name).IsUnique();

b.Entity<MoveRule>(e =>
{
    e.HasKey(r => new { r.WinnerMoveId, r.LoserMoveId });
    e.HasOne<Move>().WithMany().HasForeignKey(r => r.WinnerMoveId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Move>().WithMany().HasForeignKey(r => r.LoserMoveId).OnDelete(DeleteBehavior.Restrict);
});

b.Entity<Game>(e =>
{
    e.HasOne<Player>().WithMany().HasForeignKey(g => g.Player1Id).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Player>().WithMany().HasForeignKey(g => g.Player2Id).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Player>().WithMany().HasForeignKey(g => g.WinnerId).OnDelete(DeleteBehavior.Restrict);
});

b.Entity<Round>(e =>
{
    e.HasOne<Game>().WithMany().HasForeignKey(r => r.GameId).OnDelete(DeleteBehavior.Cascade);
    e.HasOne<Player>().WithMany().HasForeignKey(r => r.WinnerId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Move>().WithMany().HasForeignKey(r => r.Move1Id).OnDelete(DeleteBehavior.Restrict);
    e.HasOne<Move>().WithMany().HasForeignKey(r => r.Move2Id).OnDelete(DeleteBehavior.Restrict);
});
```

**`HasOne<X>().WithMany()` sin propiedad de navegación.** No hay
`public Move WinnerMove { get; set; }` en las entidades. Las navegaciones sirven para
`Include()`, y acá todas las consultas son por Id. Menos propiedades, menos superficie
para que EF haga algo inesperado.

**ERROR QUE COMETIMOS:** la primera versión no configuraba `Round → Game` ni
`Round → Player`, y la migración salió **sin esas FKs**: `GameId` quedó como un `int`
suelto. Lección: **EF infiere una FK solo si hay propiedad de navegación o configuración
explícita.** Es la contrapartida de trabajar sin navegaciones — hay que declarar *todas*
las relaciones a mano.

**`DeleteBehavior.Restrict` en casi todas.** `Game` apunta 3 veces a `Player`, `Round`
2 veces a `Move`. SQL Server rechaza cascade delete ahí con *"may cause cycles or
multiple cascade paths"*. Además es correcto: borrar un movimiento usado en partidas
históricas no debería borrar las partidas.
La excepción es `Round → Game`, que sí va en `Cascade`: una ronda huérfana no significa
nada. No genera conflicto porque todo lo que apunta a `Players` es `Restrict`.

### Seed con `HasData`

```csharp
b.Entity<Move>().HasData(
    new Move { Id = 1, Name = "Rock" },
    new Move { Id = 2, Name = "Paper" },
    new Move { Id = 3, Name = "Scissors" });

b.Entity<MoveRule>().HasData(
    new MoveRule { WinnerMoveId = 2, LoserMoveId = 1 },   // Paper beats Rock
    new MoveRule { WinnerMoveId = 1, LoserMoveId = 3 },   // Rock beats Scissors
    new MoveRule { WinnerMoveId = 3, LoserMoveId = 2 });  // Scissors beats Paper
```

EF los convierte en `INSERT` dentro de la migración: la base nace con el juego clásico
funcionando, sin que nadie siembre nada a mano. Los Ids tienen que ser **fijos** porque
EF compara contra ellos para generar futuras migraciones.

---

## Paso 5 — Generar la migración

```bash
# el tool, local al repo (queda en .config/dotnet-tools.json, se commitea)
dotnet new tool-manifest
dotnet tool install dotnet-ef --version '8.0.*'

# desde la raíz del repo
ConnectionStrings__Default="Server=localhost,1433;Database=GameOfDrones;User Id=sa;Password=...;TrustServerCertificate=True" \
dotnet ef migrations add Initial --project backend
```

**Por qué la variable de entorno adelante.** `dotnet ef` no lee tus clases: *arranca tu
aplicación* para construir el modelo. Eso ejecuta `AddDbContext(o => o.UseSqlServer(...))`,
y como la connection string vive solo en el `docker-compose.yml`, fuera del contenedor
llega `null` y `UseSqlServer(null)` explota.

Poniéndola delante del comando se la pasás **solo a ese comando** (bash/zsh), no queda
en tu shell. No conecta a nada — generar una migración es análisis estático puro — pero
EF necesita que el string exista para construir las opciones.

Notar: `localhost,1433` y no `Server=db`. `db` es el nombre del contenedor y solo resuelve
dentro de la red de Docker.

Para rehacer una migración que todavía no se aplicó: `dotnet ef migrations remove --project backend`.

---

## Paso 6 — Aplicar la migración al arrancar

En `Program.cs`, justo después de `var app = builder.Build();`:

```csharp
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
```

`Migrate()` crea la base si no existe y aplica lo pendiente.

**Atajo consciente.** Migrar al arranque no se hace en producción: con varias instancias,
todas corren el mismo `ALTER TABLE` a la vez, y un despliegue fallido deja el esquema a
medias. Lo correcto es un job separado antes del deploy. Acá lo hacemos porque el requisito
real es *"un comando y funciona"*. **Conviene mencionarlo en el README**: muestra que sabés
que es un atajo y por qué lo elegiste.

### Verificación

```bash
docker compose up --build
curl localhost:5080/health     # → true

docker compose exec db sh -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT * FROM GameOfDrones.dbo.Moves"'
```

**Ojo con `docker compose exec`:** no pasa por un shell, ejecuta el binario directo y no
expande variables. Si escribís `-P "$SA_PASSWORD"` lo expande **tu zsh** (donde no existe:
el `.env` lo lee Compose, no tu shell) y mandás password vacía → `Login failed for user 'sa'`.
Por eso el `sh -c '...'` con comillas simples. Es el mismo motivo del `$$` en el healthcheck,
visto desde el otro lado.

**Estado: backend en pie, base creada, seed cargado.**

---

## Paso 7 — Endpoints (minimal APIs)

Todo en `backend/Endpoints/ApiEndpoints.cs` como método de extensión (`app.MapApi()`),
con los DTOs en `Endpoints/Dtos.cs`. Así `Program.cs` queda en 20 líneas.

### Contratos

```
GET    /api/moves                                  → [{id, name}]
POST   /api/moves    {name}                        → crear "Dog"
GET    /api/rules                                  → reglas con nombres
POST   /api/rules    {winnerMoveId, loserMoveId}
DELETE /api/rules/{winnerId}/{loserId}
POST   /api/games    {player1Name, player2Name}    → {gameId}
POST   /api/games/{id}/rounds  {move1Id, move2Id}  → resultado de la ronda
GET    /api/stats                                  → [{name, gamesWon}]
```

### El corazón: resolver una ronda

```csharp
int? roundWinnerId = null;
if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.Move1Id && r.LoserMoveId == req.Move2Id))
    roundWinnerId = game.Player1Id;
else if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.Move2Id && r.LoserMoveId == req.Move1Id))
    roundWinnerId = game.Player2Id;
```

**Este bloque no menciona ni un solo nombre de movimiento.** Dos consultas y `null` si
ninguna regla aplica. Agregar "Dog" no toca una línea de C#. Ahí está toda la feature de
reglas en runtime.

**No hace falta chequear `move1 == move2` por separado.** Si son el mismo movimiento
ninguna de las dos reglas existe (nadie se mata a sí mismo) y cae en empate solo.
Un caso menos que mantener.

### Reglas que no se pueden saltear

**El score se recalcula desde la tabla `Rounds`, siempre.** Nunca se manda desde el front
ni se guarda en una columna del `Game`. Si el cliente puede decirte el marcador, el
cliente puede ganar solo.

**El `SaveChangesAsync` del round va ANTES de contar las victorias.** Si contás primero,
la ronda recién creada todavía no está en la base y el marcador queda un punto atrás:
el juego nunca llega a 3 y nunca termina. Bug silencioso.

**Jugadores por nombre, reutilizados** (`GetOrCreatePlayer`). Sin esto "Fazo" sería un
jugador nuevo en cada partida y las estadísticas darían siempre 1. Ese es el sentido del
índice único en `Player.Name`.

**Rechazar ciclos en las reglas.** Si existe `A mata B`, `POST /rules` con `B mata A`
devuelve 409. Sin esta validación los dos ganarían la ronda y el resultado dependería
del orden en que consultes. Es el bug que aparece recién cuando el evaluador prueba
la feature de reglas custom.

**Validación en la frontera:** nombres no vacíos, nombres distintos entre sí, movimientos
existentes, game existente, game no terminado (409). Sin esto un `move1Id: 999` tira una
`DbUpdateException` fea en vez de un 400.

### Detalles de EF que mordieron

**`FindAsync(winnerId, loserId)` con clave compuesta** — el orden de los argumentos debe
coincidir con el del `HasKey(r => new { r.WinnerMoveId, r.LoserMoveId })`. Invertidos,
compila igual y busca la regla equivocada.

**ERROR QUE COMETIMOS — `/stats` no traducía a SQL.** La primera versión era:

```csharp
.Select(p => new StatDto(p.Name, db.Games.Count(g => g.WinnerId == p.Id)))
.OrderByDescending(s => s.GamesWon)      // ← explota en runtime
```

`s.GamesWon` es una propiedad de un `record` de C#: en SQL no existe. EF no puede ordenar
por algo que solo cobra forma después de materializar el resultado. Compila perfecto y
falla en tiempo de ejecución, porque el compilador de C# no sabe qué es traducible a SQL.

Solución: ordenar **antes** de proyectar, con la expresión real.

```csharp
await db.Players
    .OrderByDescending(p => db.Games.Count(g => g.WinnerId == p.Id))
    .ThenBy(p => p.Name)
    .Select(p => new StatDto(p.Name, db.Games.Count(g => g.WinnerId == p.Id)))
    .ToListAsync();
```

El `Count` repetido no es problema: SQL Server lo resuelve en la misma pasada. La
alternativa (`ToListAsync()` y ordenar en memoria) trae toda la tabla al servidor para
ordenarla ahí: con 4 jugadores da igual, con 40.000 no.
El `ThenBy(p => p.Name)` hace determinista el orden cuando hay empates — sin él SQL Server
puede devolver los empatados en orden distinto entre llamadas y la tabla del front parpadea.

**`p.Name == name` es case-insensitive** porque la collation por defecto de SQL Server lo es.
"fazo" encuentra al "Fazo" existente. Es comportamiento de la base, no del código:
en PostgreSQL SÍ distingue mayúsculas y habría que normalizar a mano.

### Persistencia de los datos: falta el volumen

Sin volumen, la base vive dentro del contenedor y `docker compose down` borra todas las
partidas. Como el requisito es "saber cuántas partidas ganó cada jugador", hay que
declararlo:

```yaml
  db:
    volumes:
      - mssql-data:/var/opt/mssql

volumes:
  mssql-data:
```

Para empezar de cero a propósito: `docker compose down -v`.

### Verificación del flujo completo

```
ronda 1  Rock vs Scissors  → gana Fazo          (1-0)
ronda 2  Rock vs Rock      → empate, null       (1-0)
ronda 3  Paper vs Rock     → gana Fazo          (2-0)
ronda 4  Scissors vs Paper → gana Fazo, gameWinner: "Fazo"
ronda 5  sobre game terminado → HTTP 409
```

Y la feature que evalúan de verdad:

```
POST /api/moves {"name":"Dog"}              → 200
POST /api/rules {Dog mata Paper}            → 204
POST /api/rules {Paper mata Dog}            → 409 (ciclo rechazado)
ronda Dog vs Paper → gana Dog
ronda Dog vs Rock  → empate (ninguna regla los relaciona)
```

**Estado: backend completo y verificado. Sigue el frontend.**
---

## Paso 8 — Frontend: scaffold y decisiones de base

```bash
nvm use 24
ng new frontend --style=css --ssr=false --skip-git --routing
npm install bootstrap
```

- `--ssr=false`: es un juego de dos personas frente a una pantalla. No hay nada que
  indexar ni primer render que optimizar; SSR agregaría un servidor Node al stack a
  cambio de nada.
- `--skip-git`: el repo ya existe en la raíz, no querés uno anidado.

**Dónde estaba el Angular CLI.** `which ng` no encontraba nada aunque estaba instalado:
los paquetes globales de npm son **por versión de Node**, no por máquina. El CLI 21.2.3
vivía bajo Node v24, y el alias default de nvm apuntaba a la 22. Por eso `.nvmrc` con
`24` en la raíz: es el equivalente en Node del tool manifest de `dotnet-ef`.

### Angular 21 es zoneless por defecto

Sin `zone.js`, Angular ya no se entera sola de que algo cambió después de un callback
asíncrono. Esto **no** impide usar RxJS, pero cambia cómo el resultado llega a la vista:

```ts
// ❌ la vista puede NO actualizarse
this.http.get<Move[]>('/api/moves').subscribe(m => this.moves = m);

// ✅ escribir una signal SÍ notifica a Angular
this.http.get<Move[]>('/api/moves').subscribe(m => this.moves.set(m));

// ✅ el async pipe también
moves$ = this.http.get<Move[]>('/api/moves');   // template: moves$ | async

// ✅ toSignal: RxJS en el servicio, signal en el componente
moves = toSignal(this.http.get<Move[]>('/api/moves'), { initialValue: [] });
```

**El patrón elegido en este proyecto: RxJS para los datos, signals para el estado.**
Los `subscribe` escriben en signals, y escribir una signal es lo que dispara el redibujado.

### Bootstrap: solo el CSS

```json
"styles": [
  "node_modules/bootstrap/dist/css/bootstrap.min.css",
  "src/styles.css"
]
```

Sin el JS de Bootstrap. Su bundle existe para dropdowns, modales y tabs interactivas;
acá las tabs las hace el router, así que serían ~80 KB para nada.

### El proxy: por qué no hay CORS ni URLs en el código

`frontend/proxy.conf.json`:

```json
{ "/api": { "target": "http://localhost:5080", "secure": false } }
```

y en `angular.json`, `architect.serve.options`: `"proxyConfig": "proxy.conf.json"`.

El front **siempre** llama a rutas relativas (`/api/moves`). El origen se resuelve distinto
según el entorno:

- **desarrollo** (`ng serve` en :4200) → el proxy de Angular reenvía `/api` a :5080
- **Docker** → nginx sirve el Angular compilado y reenvía `/api` al contenedor `api`

En los dos casos el navegador ve **un solo origen**: no hay CORS que configurar en la API
y no hay ninguna URL absoluta en el código. Esto resuelve el *"avoid hard-coded URLs"*
del enunciado con menos código que la alternativa, no con más.

---

## Paso 9 — Estructura del frontend

```
src/app/
├── app.ts / app.html        ← shell: tabs + <router-outlet />
├── app.routes.ts
├── app.config.ts            ← providers
├── interfaces/
│   └── game.interfaces.ts   ← los DTOs que viajan por HTTP
├── services/
│   └── api.service.ts       ← todas las llamadas, tipadas
└── components/
    ├── game/                ← el juego (máquina de estados)
    ├── rules/               ← movimientos y reglas en runtime
    └── stats/               ← partidas ganadas
```

### `app.config.ts`

```ts
providers: [
  provideBrowserGlobalErrorListeners(),
  provideRouter(routes),
  provideHttpClient(withFetch())     // ← agregado por nosotros
]
```

Sin `provideHttpClient`, inyectar `HttpClient` tira `NullInjectorError` en runtime.
`withFetch()` usa la Fetch API en lugar del `XMLHttpRequest` histórico.

### `app.routes.ts`

```ts
{ path: '', loadComponent: () => import('./components/game/game') },
...
{ path: '**', redirectTo: '' }
```

`loadComponent` con `import()` a secas funciona porque los componentes usan
`export default`. Si fueran `export class`, haría falta `.then(m => m.Game)`.

Cada tab se descarga cuando se visita (se ve en el build: chunks `game`, `rules`, `stats`
separados). El `**` al final atrapa cualquier ruta inexistente.

### Las tabs — el detalle que se rompe siempre

```html
<a class="nav-link" routerLink="/" routerLinkActive="active"
   [routerLinkActiveOptions]="{ exact: true }">Game</a>
```

**`exact: true` solo en la primera.** Sin eso, la ruta `''` es prefijo de todas las demás
y la tab "Game" queda marcada incluso estando en Rules o Stats.

### `services/api.service.ts`

```ts
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  moves() { return this.http.get<Move[]>('/api/moves'); }
  ...
}
```

- `providedIn: 'root'` → una sola instancia para toda la app, sin declararla en ningún lado.
- `inject()` en vez de constructor: es la forma moderna, y evita el ruido del constructor.
- **Cada método devuelve el `Observable` crudo, sin `subscribe` adentro.** El servicio no
  decide *cuándo* se ejecuta la llamada; eso es del componente. Un servicio que se
  auto-suscribe no se puede componer con operadores.
- Los tipos genéricos (`get<Move[]>`) no validan nada en runtime: le dicen a TypeScript
  qué esperar. Si el backend cambiara la forma, el compilador no se entera — por eso
  las interfaces tienen que reflejar los DTOs de .NET (camelCase, como los serializa).

**Choque de nombres:** el CLI de Angular 21 genera la clase como `Game` (sin sufijo
`Component`), y había una interfaz `Game`. Se renombró la interfaz a `GameDto`, que
además dice la verdad: es la forma que viaja por HTTP, no una entidad del dominio.

---

## Paso 10 — El componente del juego

### La máquina de estados

```ts
type Phase = 'setup' | 'p1' | 'p2' | 'done';
phase = signal<Phase>('setup');
```

Cuatro fases, un `@switch` en el template:

```
'setup'  → dos inputs de nombres + Start
'p1'     → "Round N" + nombre del jugador 1 + select + Ok
'p2'     → lo mismo con el jugador 2   → recién acá llama a la API
'done'   → "[Nombre] is the new EMPEROR!" + Play Again
```

**Sin router para el flujo interno.** Son estados de una misma pantalla, no lugares a los
que quieras poder volver con el botón atrás del navegador.

**`@default` cubre `'p1'` y `'p2'`** porque dibujan exactamente la misma pantalla: lo único
que cambia es el nombre, que sale de un `computed`:

```ts
currentPlayer = computed(() => this.phase() === 'p1' ? this.names().p1 : this.names().p2);
```

El enunciado pide explícitamente no repetir componentes.

### El requisito de "el otro mira para otro lado"

```ts
private move1Id = 0;   // ← privado, no se muestra en el template
```

El movimiento del jugador 1 se guarda en memoria y **no se muestra ni se envía**. La
llamada HTTP sale recién cuando el jugador 2 confirma, con los dos movimientos juntos:

```ts
this.api.playRound(this.gameId, this.move1Id, this.selectedMoveId)
```

Si se mandara la jugada de cada uno por separado, el movimiento del primero viajaría al
servidor y quedaría visible en la pestaña de red del navegador. Un jugador curioso lo ve.

### `switchMap` en vez de `subscribe` anidado

`start()` necesita dos llamadas encadenadas: crear la partida y después traer los movimientos.

```ts
this.api.newGame(this.player1, this.player2).pipe(
  switchMap(game => this.api.moves().pipe(map(moves => ({ game, moves }))))
).subscribe({ next: ({ game, moves }) => { ... } });
```

La versión intuitiva sería un `subscribe` dentro de otro `subscribe`, y eso es lo que te
marcan en una revisión: anida, pierde la cancelación y no se puede componer.
`switchMap` aplana el encadenado; el `map` adjunta el resultado de la primera llamada
para tener los dos valores juntos en el `next`.

### Los movimientos se piden al empezar CADA partida

No una sola vez al cargar el componente. Si el jugador agrega "Dog" en la tab Rules y
vuelve a jugar, el select tiene que incluirlo.

### `[ngValue]` y no `[value]`

```html
<option [ngValue]="move.id">{{ move.name }}</option>
```

Con `[value]` el select devuelve el id como **string** (`"2"`) y el backend espera un
número. `[ngValue]` preserva el tipo.

### El marcador viene del servidor

```ts
this.score.set({ p1: result.player1Wins, p2: result.player2Wins });
this.history.update(h => [...h, { number: result.number, winner: result.roundWinner }]);
```

El front nunca calcula el marcador: lo toma de la respuesta. La tabla de Score se va
llenando con lo que devuelve cada ronda, sin pedir nada extra al servidor.

`history.update(h => [...h, ...])` crea un array **nuevo**. Mutar el existente con
`push` no cambiaría la referencia y la signal no notificaría el cambio.

---

## Paso 11 — Rules y Stats

### `forkJoin` para cargar en paralelo

```ts
forkJoin({ moves: this.api.moves(), rules: this.api.rules() }).subscribe(data => { ... });
```

Las dos llamadas son independientes: salen en paralelo y el `subscribe` corre cuando
llegaron las dos. Con `subscribe` anidados serían secuenciales sin razón.

**`switchMap` vs `forkJoin`**: `switchMap` cuando la segunda llamada *depende* de la
primera (el juego); `forkJoin` cuando son independientes y necesitás las dos (Rules).

### Recargar después de cada escritura

```ts
this.api.createMove(name).subscribe({ next: () => { this.newMoveName = ''; this.load(); } });
```

En vez de actualizar el array local a mano. Es una llamada extra a cambio de no tener
nunca la pantalla desincronizada de la base. En un CRUD de 5 filas no se nota.

### `toSignal` en Stats, `subscribe` en Rules

```ts
stats = toSignal(this.api.stats(), { initialValue: [] as Stat[] });
```

No es inconsistencia: Stats solo **lee**, una vez, y `toSignal` ahorra todo el andamiaje
(se suscribe, desuscribe al destruir el componente, y expone el valor como signal).
Rules **escribe** y necesita recargar después de cada cambio, y ahí el `subscribe` con
`next`/`error` es más directo.

Como las rutas son lazy, cada navegación a /stats crea una instancia nueva → los datos
se piden de nuevo. No hace falta refrescar a mano.

### `@empty` que dice algo real

```html
} @empty {
  <tr><td class="text-muted">No rules yet — every round would be a tie.</td></tr>
}
```

Si borrás todas las reglas el juego sigue funcionando, pero ninguna ronda tiene ganador:
empate eterno. El backend lo maneja bien; el mensaje evita que parezca un bug.

---

## Problema: "Cannot open database GameOfDrones" (error 4060)

Después de agregar el volumen al compose, la API empezó a fallar con
`Login failed for user 'sa'` — pero las credenciales estaban bien.

**Diagnóstico por las edades de los contenedores:**

```
api   Up 2 hours        ← arrancó y migró contra la base VIEJA
db    Up 14 minutes     ← recreado con el volumen nuevo, vacío
```

Al agregar `volumes: mssql-data`, `docker compose up -d` recreó `db` con un volumen vacío
y se llevó puesta la base. Pero **no tocó `api`**, porque su definición no cambió y
Compose solo recrea los servicios que cambiaron. La API quedó viva con su `Migrate()`
ya ejecutado contra una base que ya no existía.

**Solución:** `docker compose restart api` — al arrancar corre `Migrate()` de nuevo y
recrea la base con su seed.

**Regla:** si tocás el servicio `db`, reiniciá también `api`. Ante la duda,
`docker compose up -d --force-recreate`.

**Y la lección de fondo:** esto es exactamente la debilidad del atajo de migrar al
arrancar. La base solo se crea si la API arranca *después* de la base. En el flujo normal
(`docker compose up` en frío) el healthcheck garantiza ese orden y nunca falla; se rompió
al recrear solo la mitad del stack. La versión robusta es un job de migración separado
que corre antes de levantar la API. **Buena respuesta para la entrevista.**

---

## Pendiente

- Dockerfile del frontend (nginx) + servicio `web` en el compose
- Borrar `src/app/app.spec.ts` (el test del CLI espera "Hello, frontend" y rompe `ng test`)
- Un test real sobre la resolución de rondas en el backend
- README final y despliegue
