# 🎮 MeshViewer3D - Éditeur NavMesh WoW 3.3.5a

![Organisation image](Resources/Org.png)

> **Éditeur professionnel de navmesh** recréant les fonctionnalités du **Tripper.Renderer** d'Honorbuddy

[![Version](https://img.shields.io/badge/version-0.5.0--alpha-orange)](CHANGELOG.md)
[![Status](https://img.shields.io/badge/status-alpha-yellow)]()
[![Blackspots](https://img.shields.io/badge/blackspots-100%25-brightgreen)](DOCUMENTATION.md)
[![Completion](https://img.shields.io/badge/completion-45%25-blue)](IMPLEMENTATION_STATUS.md)

---

## 🚀 Quick Start

### Installation

**Prérequis**:
- Windows 10/11
- .NET 6.0 Runtime
- GPU avec OpenGL 3.3+

**Lancement**:
```bash
cd MeshViewer3D
dotnet run
```

### Premier Pas

1. **Charger une tile**: Menu Map → Load Tile → Sélectionner `.mmtile`
2. **Activer mode blackspot**: Appuyer sur `B`
3. **Placer un blackspot**: Clic gauche sur le terrain
4. **Sauvegarder**: `Ctrl+S`

📖 **[Guide Complet →](DOCUMENTATION.md)**

---

## ✨ Fonctionnalités

### ✅ Implémenté (Production-Ready)

#### 🔴 Éditeur Blackspots - 100%
- ✅ Création par clic 3D
- ✅ Drag & drop pour déplacer
- ✅ Ajustement rayon/hauteur avec molette
- ✅ Édition propriétés complète
- ✅ Save/Load format XML Honorbuddy
- ✅ Animation feedback création

#### 🎨 Visualisation NavMesh - 95%
- ✅ Rendu navmesh multi-couleurs
- ✅ Wireframe overlay
- ✅ OffMesh connections
- ✅ Lighting dynamique
- ✅ Caméra orbite professionnelle
- ✅ Info overlay temps réel

### ❌ En Développement

- ❌ Jump Links / OffMesh Editor
- ❌ Convex Volumes Editor
- ❌ WMO Blacklist
- ❌ Undo/Redo System
- ❌ Test Navigation


---

## 🖱️ Contrôles

### Caméra
- **Orbite**: Middle Mouse Drag
- **Pan**: Right Mouse Drag
- **Zoom**: Scroll Wheel
- **Reset**: `R`

### Édition Blackspot
- **Mode placement**: `B`
- **Placer**: Left Click (en mode)
- **Déplacer**: Left Drag (sur blackspot)
- **Rayon**: `Shift + Wheel`
- **Hauteur**: `Ctrl + Wheel`
- **Supprimer**: `Delete`

### Raccourcis
- `Ctrl+S` - Sauvegarder
- `Ctrl+O` - Ouvrir
- `W` - Toggle wireframe
- `L` - Toggle lighting
- `Escape` - Désélectionner

⌨️ **[Raccourcis Complets →](DOCUMENTATION.md#⌨️-raccourcis-clavier-complets)**

---

## 📚 Documentation

### Pour Utilisateurs
- 📖 **[DOCUMENTATION.md](DOCUMENTATION.md)** - Guide utilisateur complet


---

## 🎯 Statut Projet

### Version Actuelle: 0.5.0 Alpha

```
█████████████░░░░░░░░░░░░░░░ 45% COMPLET

✅ Production-Ready:
   • Blackspots Editor (100%)
   • NavMesh Viewer (95%)

❌ À Implémenter:
   • Jump Links Editor (0%)
   • Convex Volumes Editor (0%)
   • Undo/Redo System (0%)
```

### Roadmap v1.0

| Milestone | ETA | Statut |
|-----------|-----|--------|
| ✅ Blackspots Editor | Done | 100% |
| 🔵 Jump Links | +5j | 0% |
| 🟢 Convex Volumes | +6j | 0% |
| 🔧 System Robustness | +3j | 0% |
| 🎨 Polish & Advanced | +5j | 0% |

**Total pour v1.0**: ~19 jours


---

---

## 🔴 Blackspots - Exemple

```xml
<!-- Format XML Honorbuddy -->
<?xml version="1.0" encoding="utf-8"?>
<Blackspots>
  <Blackspot X="1234.56" Y="7890.12" Z="345.67" 
             Radius="10.00" Height="20.00" 
             Name="Zone Buggée" />
</Blackspots>
```

**Workflow**:
1. Charger navmesh (`.mmtile`)
2. Activer mode blackspot (`B`)
3. Cliquer pour placer
4. Flash vert confirme création
5. Ajuster avec `Shift+Wheel` / `Ctrl+Wheel`
6. Sauvegarder (`Ctrl+S`)

📖 **[Guide Blackspots →](DOCUMENTATION.md#🔴-blackspots---détails-techniques)**

---

## 🏗️ Architecture

```
MeshViewer3D/
├── Core/              # Parser, coordonnées, logique
│   ├── MmtileLoader.cs
│   ├── NavMeshData.cs
│   ├── CoordinateSystem.cs
│   └── EditableElements.cs
│
├── Data/              # Structures (Blackspot, Volume, etc.)
│   ├── Blackspot.cs
│   ├── ConvexVolume.cs
│   ├── OffMeshConnection.cs
│   └── AreaTypeInfo.cs
│
├── Rendering/         # OpenGL, caméra, raycasting
│   ├── NavMeshRenderer.cs
│   ├── Camera.cs
│   └── RayCaster.cs
│
├── UI/                # WinForms, panels, dialogs
│   ├── MainForm.cs
│   └── BlackspotPanel.cs
│
└── IO/                # Save/Load XML/binaire
    └── BlackspotSerializer.cs
```

**Technologies**:
- .NET 6.0-windows
- OpenTK 4.8.2 (OpenGL)
- Windows Forms
- Recast/Detour navmesh

---

## 📊 Comparaison avec Tripper.Renderer

| Feature | Tripper | MeshViewer3D |
|---------|---------|--------------|
| Visualisation navmesh | ✅ | ✅ 95% |
| Blackspots | ✅ | ✅ 100% |
| Jump Links | ✅ | ❌ 0% |
| Convex Volumes | ✅ | ❌ 0% |
| WMO Blacklist | ✅ | ❌ 0% |
| Test Navigation | ✅ | ❌ 0% |

**Verdict**: Excellent viewer + éditeur blackspots complet. Jump Links et Volumes à implémenter pour parité HB.

---

## 🤝 Contribution

### Priorités Développement

**Haute Priorité** ⭐⭐⭐:
1. Jump Links Editor (5 jours)
2. Convex Volumes Editor (6 jours)
3. Undo/Redo System (3 jours)

**Moyenne Priorité** ⭐⭐:
- WMO Blacklist (2 jours)
- Multi-sélection (1 jour)

**Basse Priorité** ⭐:
- Test Navigation (4 jours)
- Per-Model Volumes (3 jours)
- Polish UI (3 jours)


---


<div align="center">

### 👉 [COMMENCER MAINTENANT](DOCUMENTATION.md) 👈

[Documentation](DOCUMENTATION.md)

</div>
