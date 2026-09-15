# Game of Drones

Piedra, papel o tijera por turnos para dos jugadores en la misma pantalla, gana quien se lleve 3 rondas primero.

## Stack

- **Backend**: .NET 8 (minimal APIs) + Entity Framework Core
- **Base de datos**: SQL Server 2022
- **Frontend**: Angular 21
- **Orquestación**: Docker Compose

## Cómo ejecutarlo

Único requisito: Docker.
Muy importante, revisa el .env.example y crea un archivo .env, define el SA_PASSWORD (ejemplo correcto: FazozelaPassword1!)

```bash
docker compose up --build
```

- Aplicación: http://localhost:4200
- API (Swagger): http://localhost:5080/swagger
- Health check: http://localhost:5080/health |


Para reiniciar desde cero, borrando las partidas guardadas:

```bash
docker compose down -v
```