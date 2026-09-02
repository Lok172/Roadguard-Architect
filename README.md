# RoadGuard Architect

A 3D serious game for city planning and road safety education, built to raise awareness of traffic accident prevention through interactive, experiential learning.

## 📖 Overview

Road traffic accidents remain a critical public safety issue, with thousands of fatal accidents recorded annually. RoadGuard Architect addresses this by adopting a city-building simulation approach: players take on the role of a city mayor and strategically place traffic infrastructure, such as **traffic lights**, **stop signs**, and **speed bumps**, to reduce simulated accident rates while managing limited capital and maintaining public happiness.

The game combines gamification principles with real-world traffic engineering logic to make road safety education more engaging, memorable, and effective than conventional teaching methods.

## 🎮 Gameplay

* Players are given a city map with intersections requiring different types of traffic infrastructure.
* Correctly placing the right device at the right intersection type reduces the **Accident Rate** and raises **Happiness**.
* Happiness generates daily **Tax Revenue**, which replenishes **Capital** for further infrastructure spending.
* The **Accident Rate** naturally drifts upward over time, creating urgency and preventing passive play.
* The game features **3 progressively difficult levels**, each with different starting Capital, Happiness, and Accident Rate values.
* A **Level Results** screen provides feedback through days used, an accident trend graph, infrastructure effectiveness percentages, and a weighted **Safety Score**.
* A **global leaderboard** ranks players by Safety Score for each level.

## 🛠️ Tech Stack

|Layer|Technology|
|-|-|
|Game Client|**Unity (C#)**|
|Backend API|**ASP.NET Core**|
|ORM|**Entity Framework Core**|
|Database|**PostgreSQL**|
|Architecture Pattern|**MVVM (Model-View-ViewModel)**|
|Deployment|**AWS EC2**|

## 🏗️ System Architecture

```
Unity Client  <->  ASP.NET Core API  <->  PostgreSQL Database
   (MVVM)           (REST endpoints)      (Players, LevelResults)
```

* **Unity Client**: Handles simulation, infrastructure placement, HUD, scoring, and local gameplay logic.
* **ASP.NET Core API**: Exposes REST endpoints for authentication, leaderboard queries, and level result submission.
* **PostgreSQL Database**: Persists player profiles (`Players` table) and per-level results (`LevelResults` table).

## 🔌 API Endpoints

### Auth

|Method|Endpoint|Description|
|-|-|-|
|`POST`|`/api/auth/register`|Registers a new player by username, or returns the existing player if the username already exists|

### Leaderboard

|Method|Endpoint|Description|
|-|-|-|
|`GET`|`/api/leaderboard/top10/{level}`|Returns the top 10 ranked players for a given level, ordered by Safety Score|
|`GET`|`/api/leaderboard/player/{playerId}/level/{level}`|Returns a specific player's best Safety Score for a given level|
|`GET`|`/api/leaderboard/rank/{playerId}/level/{level}`|Returns a specific player's rank on the leaderboard for a given level|

### Results

|Method|Endpoint|Description|
|-|-|-|
|`POST`|`/api/results`|Submits a new level result (PlayerId, LevelNumber, SafetyScore, DaysUsed)|

### Health

|Method|Endpoint|Description|
|-|-|-|
|`GET`|`/api/health`|Basic health check endpoint to confirm the API is running|

**Example response for `/api/leaderboard/top10/{level}`:**

```json
[
  {
    "rank": 1,
    "playerId": 9,
    "playerName": "Lokun",
    "safetyScore": 10000,
    "daysUsed": 10
  }
]
```

## 🗄️ Database Schema (Summary)

**Players**

|Column|Type|
|-|-|
|PlayerID|integer (PK)|
|PlayerName|varchar(50)|
|CreatedAt|timestamp|

**LevelResults**

|Column|Type|
|-|-|
|Id|integer (PK)|
|PlayerId|integer (FK to Players, cascade delete)|
|LevelNumber|integer|
|SafetyScore|integer|
|DaysUsed|integer|
|CompletedAt|timestamp|

## 🚀 Getting Started

### Prerequisites

* **Unity 6000.3.11f1** or later
* **.NET SDK 8.0** or later
* **PostgreSQL 13** or later
* **Entity Framework Core CLI tools** (`dotnet-ef`)

### Backend setup steps

```bash
# Clone the repository
git clone https://github.com/Lok172/Roadguard-Architect.git
cd Roadguard-Architect/RoadguardAPI

# Restore NuGet packages
dotnet restore

# Install the EF Core CLI tool if not already installed
dotnet tool install --global dotnet-ef

# Apply the database schema (creates Players, LevelResults, and
# EFMigrationsHistory tables via EF Core migrations)
dotnet ef database update
```

> If you would rather restore the existing dataset from the provided database dump instead of running migrations on an empty database, use `pg_restore` (the dump is in PostgreSQL custom format, not plain SQL, so it cannot be run with `psql -f`):
> ```bash
> pg_restore -h localhost -U postgres -d RoadGuard --clean --if-exists Data.sql
> ```

### Database connection string / environment variable setup

Add a PostgreSQL connection string to `appsettings.json` (or `appsettings.Development.json` for local development) inside the `RoadguardAPI` project:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=RoadGuard;Username=postgres;Password=[your-password]"
  }
}
```

For production, avoid committing credentials and instead set the connection string as an environment variable, which ASP.NET Core will pick up automatically:

```bash
export ConnectionStrings_DefaultConnection="Host=<img width="1280" height="720" alt="Tutorial  (9)" src="https://github.com/user-attachments/assets/4fd96e7d-d122-4192-981d-7c2ca7a8f135" />
<img width="797" height="455" alt="Tutorial  (1)" src="https://github.com/user-attachments/assets/4051872b-c719-4658-9573-fc56e5d92867" />
[your-host];Port=5432;Database=RoadGuard;Usernameyour-username];Password=[your-password]"
```

### Commands to run the backend API

```bash
cd Roadguard-Architect/RoadguardAPI
dotnet run
```

By default this starts the API on the port configured in `launchSettings.json` (commonly `http://localhost:5000` or `https://localhost:5001` for local development). Confirm it is running by hitting the health check endpoint:

```bash
curl http://localhost:5000/api/health
```

### Unity project setup steps

1. Install **Unity Hub** if you do not already have it.
2. In Unity Hub, select **Add** > **Add project from disk**, and choose the root of the cloned `Roadguard-Architect` folder (the same folder that contains `Assets`, `Packages`, and `ProjectSettings`).
3. Unity Hub will prompt you to install the matching Unity Editor version if it is not already installed. Allow it to install.
4. Once the project opens, locate the API base URL setting in the project (typically a configuration script or `ScriptableObject` asset used by the networking layer) and point it to your running backend, e.g. `http://localhost:5000` for local development or the deployed AWS EC2 address for production.
5. Open the main gameplay scene from the `Scenes` folder in the Project window.

### Instructions to run the Unity project

1. With the correct scene open, press the **Play** button in the Unity Editor toolbar to run the game in the Editor.
2. To produce a standalone build, go to **File > Build Settings**, select your target platform, and click **Build** (or **Build and Run** to launch it immediately after building).

Alternatively, if a pre-built version is provided, simply open the `Roadguard Architect` folder and run `Roadguard Architect.exe` to play the game directly without opening Unity.

## 📸 Screenshots
<img width="797" height="455" alt="Tutorial  (1)" src="https://github.com/user-attachments/assets/76faa7fc-4784-4263-89c9-af38c1cd0181" />
*Players place traffic infrastructure and manage Capital and Happiness while the accident rate is simulated in real time.*

*<img width="1280" height="720" alt="Tutorial  (9)" src="https://github.com/user-attachments/assets/a6ad2bbb-c485-4004-9b6b-2603c28326f2" />
*The level outcome screen shows whether the mission was a Victory or a Loss.


