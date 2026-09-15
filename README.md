# Game of Drones

Piedra, papel o tijera por turnos para dos jugadores en la misma pantalla, gana quien se lleve 3 rondas primero.

## Stack

- **Backend**: .NET 8 (minimal APIs) + Entity Framework Core
- **Base de datos**: SQL Server 2022
- **Frontend**: Angular 21
- **Orquestación**: Docker Compose

## Cómo ejecutarlo

Único requisito: Docker.

### 1. Crea el archivo .env, ejecuta el siguiente comando:

```bash
cp .env.example .env
```

Muy importante, define el SA_PASSWORD correcto.

Ejemplo válido: `Str0ng!Passw0rd`

### 2. Levanta todo

```bash
docker compose up --build
```

- Aplicación: http://localhost:4200
- API (Swagger): http://localhost:5080/swagger
- Health check: http://localhost:5080/health

### 3. Juega una partida

Entra a http://localhost:4200, ingresa los dos nombres y presiona Start.

## Tests

```bash
dotnet test               # 6 tests, resolución de rondas
cd frontend && npm test   # 8 tests, servicio de API y flujo del juego
```

## Empezar de cero

Borra las partidas guardadas y vuelve a dejar solo Rock, Paper y Scissors:

```bash
docker compose down -v
```