# UrbanRoadworks

Web GIS application for managing urban roadwork sites.

***

## Overview

UrbanRoadworks is a browser-based map tool for visualizing and managing roadwork construction sites in an urban area. The application displays a real road network on an interactive map based on OpenStreetMap data, allowing operators to draw sites, assets, cable canals and walls directly on the map, calculate optimized road routes, and plan cable installations.

***

## Features

### Interactive map
- Full road network imported from OSM, with highlighting of segments affected by active or planned construction sites
- Layer visibility toggles for sites, assets, canals, walls and road network

### Roadwork sites
- Draw polygonal site areas directly on the map
- Manage name and status (`planned`, `active`, `completed`)
- Edit geometry, move and delete sites

### Assets
- Place point assets (equipment, access points, etc.) on the map
- Automatically associated to the nearest site

### Cable canals and walls
- Draw linear canal features (cable ducts) with endpoint snapping
- Draw wall features (building walls the cable must cross)
- Automatically associated to the nearest sites

### Routing
- **A to B route**: shortest path between two map points using `pgr_dijkstra`
- **Inspector tour**: optimized multi-site visit route (nearest-neighbour heuristic + pgRouting legs)

### Cable plan
- Select a set of canals and compute a cable installation plan
- Output includes total cable meters, UTP segments, nodes needed, estimated work time, and wall crossing details

### Filters and spatial queries
- Filter features by drawing a rectangular area on the map
- Find the nearest roadwork sites to a selected point

***

## Running from Visual Studio

### Prerequisites
- [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **ASP.NET and web development** workload
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 14+ with the `PostGIS` and `pgRouting` extensions enabled
- Road network data imported into the `road_network` table (EPSG:3857) with pgRouting topology already built

### 1. Clone the repository

```bash
git clone https://github.com/frameperminute/UrbanRoadworks.git
```

### 2. Configure the database connection

Open `UrbanRoadworks/appsettings.json` and set your PostgreSQL credentials:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=UrbanRoadworks;Username=YOUR_USER;Password=YOUR_PASSWORD"
}
```

### 3. Apply migrations

Open the **Package Manager Console** (Tools → NuGet Package Manager → Package Manager Console) and run:

```powershell
Update-Database
```

### 4. Run the application

Press **F5** or click the **https** run button in the Visual Studio toolbar.

The app opens directly on the interactive map.
