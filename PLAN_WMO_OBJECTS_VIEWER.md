# 🎯 PLAN COMPLET - Ajout Visualisation Objets 3D (WMO/M2)

> **Objectif**: Reproduire la visualisation des objets 3D comme dans HB Tripper.Renderer
> **Référence visuelle**: `image tripper.renderer/` (9 screenshots)
> **Client WoW cible**: **3.3.5a (build 12340)** - Wrath of the Lich King
> **Chemin client utilisateur**: `C:\Users\Texy\Desktop\World of Warcraft 3.3.5a`

---

## ⚠️ VERSIONS SPÉCIFIQUES 3.3.5a

### Ce qui EXISTE dans 3.3.5a
| Élément | Version | Notes |
|---------|---------|-------|
| **Archives** | MPQ | PAS de CASC (CASC = WoD 6.0+) |
| **WMO** | v17 | Header MOHD différent des versions récentes |
| **M2** | v264-272 | Pas de chunks, format linéaire |
| **ADT** | v18 | MVER = 18, format pré-Cataclysm |
| **WDT** | v18 | Pas de flags MoP+ |
| **BLP** | BLP1 / BLP2 | Compression JPEG ou DXT |

### Ce qui N'EXISTE PAS dans 3.3.5a
| Élément | Introduit dans | Alternative 3.3.5a |
|---------|----------------|-------------------|
| CASC | WoD 6.0 | Utiliser MPQ |
| ADT v23+ | Cataclysm | ADT v18 |
| M2 chunked | MoP+ | M2 format linéaire |
| WMO v18+ | Cataclysm | WMO v17 |
| Height texturing | Cataclysm | Pas disponible |
| Phasing terrain | WoD | Pas disponible |

---

## 📚 CONTEXTE TECHNIQUE - WoW 3.3.5a SPÉCIFIQUE

### Comment WoW stocke les données 3D (version 3.3.5a)

```
World of Warcraft 3.3.5a/
├── Data/
│   ├── common.MPQ           ← Archive principale (modèles de base)
│   ├── common-2.MPQ         ← Suite archive principale
│   ├── expansion.MPQ        ← The Burning Crusade
│   ├── lichking.MPQ         ← Wrath of the Lich King
│   ├── patch.MPQ            ← Patch 3.0
│   ├── patch-2.MPQ          ← Patch 3.1
│   └── patch-3.MPQ          ← Patch 3.3.5a
```

### Formats de fichiers WoW à parser (3.3.5a SPÉCIFIQUE)

| Extension | Nom complet | Version 3.3.5a | Contenu | Priorité |
|-----------|-------------|----------------|---------|----------|
| `.mpq` | Mo'PaQ archive | MPQ v1-2 | Container de tous les fichiers | ⭐⭐⭐ |
| `.wmo` | World Map Object | **v17** | Bâtiments, grottes, structures | ⭐⭐⭐ |
| `.m2` | Model 2 | **v264-272** (linéaire) | Arbres, rochers, décors, PNJ | ⭐⭐ |
| `.adt` | Area Data Tile | **v18** (pré-Cata) | Terrain + références objets | ⭐⭐⭐ |
| `.wdt` | World Definition Table | **v18** | Liste des tiles d'une map | ⭐⭐ |
| `.blp` | Blizzard Picture | **BLP1/BLP2** | Textures compressées | ⭐ |

### Relation entre les fichiers

```
WDT (world definition)
 └── ADT[x,y] (terrain tile 533x533 yards)
      ├── MCNK chunks (16x16 = 256 par ADT)
      │    └── Heightmap + textures terrain
      ├── MODF entries (WMO placements)
      │    └── Référence vers fichier .wmo + position/rotation/scale
      └── MDDF entries (M2/doodad placements)
           └── Référence vers fichier .m2 + position/rotation/scale
```

---

## 🏗️ ARCHITECTURE PROPOSÉE

### Structure des nouveaux fichiers

```
MeshViewer3D/
├── Data/                          # EXISTANT - Structures de données
│   ├── Blackspot.cs              ✅ Existe
│   ├── ConvexVolume.cs           ✅ Existe
│   └── JumpLink.cs               ❌ À créer
│
├── Mpq/                           # NOUVEAU - Lecture archives MPQ
│   ├── MpqArchive.cs             ❌ À créer - Wrapper StormLib
│   ├── MpqFileSystem.cs          ❌ À créer - VFS pour accès fichiers
│   └── WowDataProvider.cs        ❌ À créer - Interface haut niveau
│
├── Formats/                       # NOUVEAU - Parsers formats WoW
│   ├── ChunkReader.cs            ❌ À créer - Lecture chunks IFF
│   ├── Wmo/
│   │   ├── WmoFile.cs            ❌ À créer - Parser WMO root
│   │   ├── WmoGroup.cs           ❌ À créer - Parser WMO group
│   │   └── WmoStructures.cs      ❌ À créer - Structs MVER, MOHD, etc.
│   ├── M2/
│   │   ├── M2File.cs             ❌ À créer - Parser M2
│   │   └── M2Structures.cs       ❌ À créer - Structs header, vertices
│   ├── Adt/
│   │   ├── AdtFile.cs            ❌ À créer - Parser ADT
│   │   └── AdtStructures.cs      ❌ À créer - MCNK, MODF, MDDF
│   └── Blp/
│       └── BlpTexture.cs         ❌ À créer - Parser BLP (optionnel)
│
├── World/                         # NOUVEAU - Gestion monde 3D
│   ├── WorldManager.cs           ❌ À créer - Charge/décharge tiles
│   ├── TileData.cs               ❌ À créer - Cache données par tile
│   ├── ObjectInstance.cs         ❌ À créer - Instance WMO/M2 placée
│   └── BoundingBox.cs            ❌ À créer - AABB pour culling
│
├── Rendering/                     # EXISTANT - Rendu OpenGL
│   ├── NavMeshRenderer.cs        ✅ Existe - À modifier
│   ├── WmoRenderer.cs            ❌ À créer - Rendu WMO
│   ├── M2Renderer.cs             ❌ À créer - Rendu M2
│   ├── TerrainRenderer.cs        ❌ À créer - Rendu terrain ADT
│   └── TextureCache.cs           ❌ À créer - Cache textures OpenGL
│
├── UI/                            # EXISTANT - Interface utilisateur
│   ├── MainForm.cs               ✅ Existe - À modifier
│   ├── BlackspotPanel.cs         ✅ Existe
│   ├── JumpLinkPanel.cs          ❌ À créer
│   ├── ConvexVolumePanel.cs      ❌ À créer
│   ├── GameObjectPanel.cs        ❌ À créer - Liste WMO/M2
│   └── WmoBlacklistPanel.cs      ❌ À créer
```

---

## 📦 DÉPENDANCES REQUISES

### NuGet Packages à ajouter

```xml
<!-- MeshViewer3D.csproj - Ajouter dans <ItemGroup> -->

<!-- Lecture archives MPQ (StormLib wrapper) -->
<PackageReference Include="StormLibSharp" Version="1.0.0" />
<!-- OU alternative managed pure C# -->
<PackageReference Include="MpqLib" Version="1.0.0" />

<!-- Déjà présents -->
<PackageReference Include="OpenTK" Version="4.8.2" />
<PackageReference Include="OpenTK.WinForms" Version="4.0.0" />
```

### Fichiers natifs requis (si StormLib)

```
bin/Debug/net6.0-windows/
├── StormLib.dll        ← DLL native 32/64 bits
└── StormLibSharp.dll   ← Wrapper .NET
```

---

## 🔢 PHASES DE DÉVELOPPEMENT

### PHASE 0 : Prérequis (AVANT TOUT)
**Fichiers**: Aucun nouveau
**Objectif**: Corriger bugs existants

```
[ ] Fix raycast click (No navmesh intersection found)
    → Tester double-face triangles (déjà ajouté, à compiler/tester)
[ ] Compiler et tester que click fonctionne
```

---

### PHASE 1 : Lecture MPQ (Fondation)
**Effort estimé**: ~8 heures
**Priorité**: ⭐⭐⭐ CRITIQUE

#### 1.1 MpqArchive.cs
```csharp
namespace MeshViewer3D.Mpq
{
    /// <summary>
    /// Wrapper pour lecture d'une archive MPQ unique
    /// Utilise StormLib via P/Invoke ou StormLibSharp
    /// </summary>
    public class MpqArchive : IDisposable
    {
        private IntPtr _handle;
        
        public MpqArchive(string path);
        public bool FileExists(string internalPath);
        public byte[] ReadFile(string internalPath);
        public Stream OpenFile(string internalPath);
        public IEnumerable<string> ListFiles(string pattern = "*");
        public void Dispose();
    }
}
```

#### 1.2 MpqFileSystem.cs
```csharp
namespace MeshViewer3D.Mpq
{
    /// <summary>
    /// Système de fichiers virtuel combinant plusieurs MPQ
    /// Gère la priorité des patches (patch-3 > patch-2 > patch > lichking > ...)
    /// </summary>
    public class MpqFileSystem : IDisposable
    {
        private List<MpqArchive> _archives;
        
        public MpqFileSystem(string wowDataPath);
        public byte[] ReadFile(string path);  // Cherche dans tous les MPQ
        public bool FileExists(string path);
        public void Dispose();
    }
}
```

#### 1.3 WowDataProvider.cs
```csharp
namespace MeshViewer3D.Mpq
{
    /// <summary>
    /// Interface haut niveau pour accéder aux données WoW
    /// Cache les fichiers parsés en mémoire
    /// </summary>
    public class WowDataProvider : IDisposable
    {
        private MpqFileSystem _mpq;
        private Dictionary<string, WmoFile> _wmoCache;
        private Dictionary<string, M2File> _m2Cache;
        
        public WowDataProvider(string wowInstallPath);
        
        public WmoFile GetWmo(string path);
        public M2File GetM2(string path);
        public AdtFile GetAdt(int mapId, int tileX, int tileY);
        
        public void ClearCache();
        public void Dispose();
    }
}
```

---

### PHASE 2 : Parser WMO (Bâtiments) - VERSION 17
**Effort estimé**: ~16 heures
**Priorité**: ⭐⭐⭐ CRITIQUE
**Version cible**: WMO v17 (3.3.5a)

#### Documentation format WMO
- Wiki: https://wowdev.wiki/WMO
- **Version 3.3.5a = version 17** (IMPORTANT: structure MOHD différente des versions récentes!)

#### 2.1 WmoStructures.cs (VERSION 17 - 3.3.5a)
```csharp
namespace MeshViewer3D.Formats.Wmo
{
    // ⚠️ STRUCTURES SPÉCIFIQUES WMO v17 (WotLK 3.3.5a)
    // Les versions Cataclysm+ ont des champs supplémentaires!
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MVER  // Version chunk
    {
        public uint Version;  // 17 pour WotLK 3.3.5a
    }
    
    /// <summary>
    /// MOHD - WMO Header (VERSION 17 SEULEMENT)
    /// ⚠️ Cataclysm+ ajoute des champs après flags!
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MOHD_v17
    {
        public uint nMaterials;      // Nombre de matériaux (MOMT)
        public uint nGroups;         // Nombre de groupes WMO
        public uint nPortals;        // Nombre de portails
        public uint nLights;         // Nombre de lumières
        public uint nDoodadNames;    // Taille MODN en bytes
        public uint nDoodadDefs;     // Nombre MODD entries
        public uint nDoodadSets;     // Nombre MODS entries
        public uint ambientColor;    // Couleur ambiante (BGRA)
        public uint wmoID;           // ID dans WMOAreaTable.dbc
        public Vector3 boundingBox1; // Min corner AABB
        public Vector3 boundingBox2; // Max corner AABB
        public ushort flags;         // WMO flags
        public ushort numLod;        // ⚠️ Seulement dans v17, ignoré souvent
        // PAS de champs supplémentaires dans v17!
    }
    
    // ... autres structures MOTX, MOMT, MOGN, MOGI, etc.
}
```

#### 2.2 WmoFile.cs
```csharp
namespace MeshViewer3D.Formats.Wmo
{
    /// <summary>
    /// Parser pour fichier WMO root (_root.wmo ou sans suffixe)
    /// Charge header + références aux groupes
    /// </summary>
    public class WmoFile
    {
        public MOHD Header { get; private set; }
        public string[] GroupNames { get; private set; }
        public WmoGroup[] Groups { get; private set; }
        public BoundingBox BoundingBox { get; private set; }
        
        public static WmoFile Load(byte[] data, Func<string, byte[]> groupLoader);
        
        // Retourne tous les vertices/indices pour rendu
        public (Vector3[] vertices, uint[] indices) GetRenderGeometry();
    }
}
```

#### 2.3 WmoGroup.cs
```csharp
namespace MeshViewer3D.Formats.Wmo
{
    /// <summary>
    /// Parser pour fichier WMO group (_000.wmo, _001.wmo, etc.)
    /// Contient la géométrie réelle
    /// </summary>
    public class WmoGroup
    {
        public Vector3[] Vertices { get; private set; }    // MOVT chunk
        public Vector3[] Normals { get; private set; }     // MONR chunk
        public ushort[] Indices { get; private set; }      // MOVI chunk
        public MOPY[] PolyInfo { get; private set; }       // Flags par triangle
        
        public static WmoGroup Load(byte[] data);
    }
}
```

---

### PHASE 3 : Parser ADT (Placement objets) - VERSION 18
**Effort estimé**: ~8 heures
**Priorité**: ⭐⭐⭐ CRITIQUE
**Version cible**: ADT v18 (3.3.5a, pré-Cataclysm)

#### ⚠️ DIFFÉRENCES ADT v18 vs versions récentes
- Pas de chunks MFBO (flight bounds) - ajouté en Cataclysm
- Pas de chunks MH2O (eau améliorée) dans le format standard
- Structure MCNK plus simple
- Pas de LOD terrain

#### 3.1 AdtStructures.cs (VERSION 18 - 3.3.5a)
```csharp
namespace MeshViewer3D.Formats.Adt
{
    /// <summary>
    /// MODF - Placement d'un WMO dans le monde
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MODF
    {
        public uint mwidEntry;        // Index dans MWID (nom fichier)
        public uint uniqueId;
        public Vector3 position;
        public Vector3 rotation;      // Degrés
        public Vector3 lowerBounds;
        public Vector3 upperBounds;
        public ushort flags;
        public ushort doodadSet;
        public ushort nameSet;
        public ushort scale;          // 1024 = 1.0
    }
    
    /// <summary>
    /// MDDF - Placement d'un M2/doodad dans le monde
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MDDF
    {
        public uint mmidEntry;        // Index dans MMID (nom fichier)
        public uint uniqueId;
        public Vector3 position;
        public Vector3 rotation;      // Degrés
        public ushort scale;          // 1024 = 1.0
        public ushort flags;
    }
}
```

#### 3.2 AdtFile.cs
```csharp
namespace MeshViewer3D.Formats.Adt
{
    /// <summary>
    /// Parser ADT - Extrait les placements WMO et M2
    /// Pour l'instant, on ignore le terrain (heightmap)
    /// </summary>
    public class AdtFile
    {
        public string[] WmoNames { get; private set; }   // MWMO chunk
        public string[] M2Names { get; private set; }    // MMDX chunk
        public MODF[] WmoInstances { get; private set; } // MODF chunk
        public MDDF[] M2Instances { get; private set; }  // MDDF chunk
        
        public static AdtFile Load(byte[] data);
    }
}
```

---

### PHASE 4 : Rendu WMO OpenGL
**Effort estimé**: ~12 heures
**Priorité**: ⭐⭐

#### 4.1 WmoRenderer.cs
```csharp
namespace MeshViewer3D.Rendering
{
    /// <summary>
    /// Rendu OpenGL des WMO
    /// Mode simple: couleur unie ou wireframe (pas de textures)
    /// </summary>
    public class WmoRenderer : IDisposable
    {
        private int _vao, _vbo, _ebo;
        private int _vertexCount;
        private int _indexCount;
        
        public bool ShowWireframe { get; set; }
        public Color4 Color { get; set; } = new Color4(0.6f, 0.6f, 0.7f, 0.5f);
        
        public void LoadWmo(WmoFile wmo, Matrix4 worldTransform);
        public void Render(Matrix4 view, Matrix4 projection);
        public void Dispose();
    }
}
```

#### 4.2 Modification NavMeshRenderer.cs
```csharp
// Ajouter dans NavMeshRenderer:

public bool ShowWmoObjects { get; set; } = true;
public bool ShowM2Objects { get; set; } = true;

private List<WmoRenderer> _wmoRenderers = new();
private List<M2Renderer> _m2Renderers = new();

public void LoadWorldObjects(WowDataProvider dataProvider, AdtFile adt)
{
    // Charger et créer renderers pour chaque WMO/M2 du ADT
    foreach (var modf in adt.WmoInstances)
    {
        var wmo = dataProvider.GetWmo(adt.WmoNames[modf.mwidEntry]);
        var renderer = new WmoRenderer();
        renderer.LoadWmo(wmo, CreateTransformMatrix(modf));
        _wmoRenderers.Add(renderer);
    }
    // Pareil pour M2...
}
```

---

### PHASE 5 : UI GameObjects Panel
**Effort estimé**: ~4 heures
**Priorité**: ⭐⭐

#### 5.1 GameObjectPanel.cs
```csharp
namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel listant tous les WMO/M2 de la tile chargée
    /// Comme dans HB Tripper.Renderer
    /// </summary>
    public class GameObjectPanel : Panel
    {
        private TreeView _treeView;
        private CheckBox _showWmo;
        private CheckBox _showM2;
        
        public event Action<bool>? WmoVisibilityChanged;
        public event Action<bool>? M2VisibilityChanged;
        public event Action<string>? ObjectSelected;
        
        public void LoadObjects(AdtFile adt);
        public void Clear();
    }
}
```

---

### PHASE 6 : Jump Links Editor (CRITIQUE pour bot)
**Effort estimé**: ~8 heures
**Priorité**: ⭐⭐⭐ CRITIQUE

#### 6.1 JumpLink.cs (Data)
```csharp
namespace MeshViewer3D.Data
{
    /// <summary>
    /// Jump Link = connexion de navigation spéciale
    /// Permet au bot de sauter d'un point A à un point B
    /// </summary>
    public struct JumpLink
    {
        public Vector3 Start;      // Point de départ (Detour coords)
        public Vector3 End;        // Point d'arrivée
        public float Width;        // Largeur du passage
        public bool Bidirectional; // Peut aller dans les deux sens?
        public string Name;
        
        public JumpLink(Vector3 start, Vector3 end, float width = 2.0f)
        {
            Start = start;
            End = end;
            Width = width;
            Bidirectional = false;
            Name = "";
        }
    }
}
```

#### 6.2 JumpLinkPanel.cs (UI)
```csharp
namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel d'édition des Jump Links
    /// Mode: Clic 1 = start, Clic 2 = end
    /// </summary>
    public class JumpLinkPanel : Panel
    {
        public event Action<bool>? ClickModeChanged;  // true = mode placement
        public event Action<int>? LinkSelected;
        public event Action<int>? LinkDeleted;
        
        public void UpdateElements(EditableElements elements);
        
        // Boutons: [+ Add] [- Delete] [Bidirectional checkbox]
        // Liste: Start → End (distance)
        // Champs: Width NumericUpDown
    }
}
```

---

### PHASE 7 : Convex Volumes Editor
**Effort estimé**: ~8 heures
**Priorité**: ⭐⭐⭐ CRITIQUE

#### 7.1 ConvexVolumePanel.cs (UI)
```csharp
namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel d'édition des Convex Volumes
    /// Mode: Clic pour placer vertices du polygone
    /// Double-clic ou Enter pour fermer le polygone
    /// </summary>
    public class ConvexVolumePanel : Panel
    {
        public event Action<bool>? ClickModeChanged;
        public event Action<int>? VolumeSelected;
        public event Action<int>? VolumeDeleted;
        
        public void UpdateElements(EditableElements elements);
        
        // Boutons: [+ Add] [- Delete] [Finish Polygon]
        // Liste: Volume N (X vertices)
        // Champs: AreaType ComboBox, Height NumericUpDown
    }
}
```

---

## ✅ CHECKLIST DE PROGRESSION

### Phase 0 - Prérequis
- [ ] Fix raycast double-face (code ajouté, à tester)
- [ ] Compiler avec succès
- [ ] Tester click placement blackspot

### Phase 1 - MPQ
- [ ] Ajouter package StormLibSharp ou MpqLib
- [ ] Créer MpqArchive.cs
- [ ] Créer MpqFileSystem.cs
- [ ] Créer WowDataProvider.cs
- [ ] Test: lire un fichier depuis MPQ

### Phase 2 - WMO Parser
- [ ] Créer ChunkReader.cs
- [ ] Créer WmoStructures.cs
- [ ] Créer WmoFile.cs
- [ ] Créer WmoGroup.cs
- [ ] Test: parser un WMO et extraire vertices

### Phase 3 - ADT Parser
- [ ] Créer AdtStructures.cs
- [ ] Créer AdtFile.cs
- [ ] Test: lister WMO/M2 d'une tile

### Phase 4 - Rendu WMO
- [ ] Créer WmoRenderer.cs
- [ ] Modifier NavMeshRenderer.cs
- [ ] Test: afficher un WMO dans le viewport

### Phase 5 - UI Objets
- [ ] Créer GameObjectPanel.cs
- [ ] Ajouter onglet dans MainForm
- [ ] Test: toggle visibilité objets

### Phase 6 - Jump Links
- [ ] Créer JumpLink.cs
- [ ] Créer JumpLinkPanel.cs
- [ ] Ajouter dans EditableElements
- [ ] Ajouter rendu dans NavMeshRenderer
- [ ] Ajouter sérialisation XML
- [ ] Test: créer/éditer/sauvegarder jump links

### Phase 7 - Convex Volumes
- [ ] Créer ConvexVolumePanel.cs (UI complète)
- [ ] Ajouter mode édition vertices
- [ ] Test: créer/éditer polygones

---

## 🔗 RÉFÉRENCES

### Documentation formats WoW (⚠️ UTILISER VERSIONS 3.3.5a)
- **WMO v17**: https://wowdev.wiki/WMO#WotLK (pas la version actuelle!)
- **M2 v264-272**: https://wowdev.wiki/M2#WotLK_.28x.3E264.29 (format linéaire, PAS chunked!)
- **ADT v18**: https://wowdev.wiki/ADT/v18 (version pré-Cataclysm)
- **MPQ**: https://wowdev.wiki/MPQ (pas de CASC!)
- **BLP**: https://wowdev.wiki/BLP (BLP1/BLP2 seulement)

### Projets de référence (code source)
- **WoWModelViewer**: https://github.com/WoWModelViewer
- **Noggit**: https://github.com/noggit-red/noggit-red (éditeur de maps)
- **StormLib**: https://github.com/ladislav-zezula/StormLib

### Coordonnées WoW
```
WoW utilise un système main gauche:
- X = Nord-Sud
- Y = Est-Ouest  
- Z = Haut-Bas

Conversion WoW ↔ Detour:
Detour.X = -WoW.Y
Detour.Y = WoW.Z
Detour.Z = -WoW.X
```

---

## 📝 NOTES IMPORTANTES

### ⚠️ PIÈGES SPÉCIFIQUES 3.3.5a

1. **NE PAS utiliser de code/exemples pour versions récentes!**
   - Le format M2 est devenu "chunked" après MoP - 3.3.5a utilise format LINÉAIRE
   - WMO v17 a moins de champs que v18+
   - ADT v18 n'a pas les mêmes chunks que Cataclysm+

2. **Archives MPQ, pas CASC!**
   - CASC n'existe pas en 3.3.5a
   - Utiliser StormLib pour MPQ

3. **Ordre de lecture MPQ (priorité décroissante)**:
   ```
   patch-3.MPQ      ← Plus haute priorité
   patch-2.MPQ
   patch.MPQ
   lichking.MPQ
   expansion.MPQ
   common-2.MPQ
   common.MPQ       ← Plus basse priorité
   ```

4. **Chemins internes MPQ** (exemples 3.3.5a):
   ```
   World\wmo\Northrend\...
   World\Maps\Northrend\...
   World\Minimaps\...
   ```

### Autres notes

1. **Priorité absolue**: Jump Links et Convex Volumes sont critiques pour le bot, 
   même SANS visualisation des objets 3D.

2. **Les objets 3D sont un "nice to have"**: Le bot fonctionne avec juste le navmesh.
   La visualisation aide à comprendre POURQUOI le navmesh a certaines formes.

3. **Textures optionnelles**: Commencer par rendu solid/wireframe sans textures.
   Les textures BLP ajoutent de la complexité pour peu de valeur fonctionnelle.

4. **Cache important**: Les WMO peuvent être volumineux. Implémenter un cache LRU.

5. **Thread-safety**: Le parsing peut être lent. Considérer chargement async.
