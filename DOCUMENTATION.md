# 📚 DOCUMENTATION COMPLÈTE - MeshViewer3D Editor

## 🎯 VUE D'ENSEMBLE

**MeshViewer3D** est un éditeur complet de navmesh pour WoW 3.3.5a, recréant les fonctionnalités du **Tripper.Renderer** d'Honorbuddy. Il permet de visualiser, créer et éditer des éléments de navigation (blackspots, jump links, convex volumes).

---

## 🖱️ CONTRÔLES ET INTERACTIONS

### Caméra

| Action | Contrôle |
|--------|----------|
| **Orbiter** | `Middle Mouse Drag` ou `Left Mouse Drag` (si aucun élément sélectionné) |
| **Pan (déplacement latéral)** | `Right Mouse Drag` |
| **Zoom** | `Scroll Wheel` (sans modificateur) |
| **Reset caméra** | `R` ou Menu View → Reset Camera |

### Sélection

| Action | Contrôle |
|--------|----------|
| **Sélectionner blackspot** | `Left Click` sur le cylindre |
| **Désélectionner** | `Escape` ou `Left Click` dans le vide |
| **Supprimer sélection** | `Delete` |

### Édition Blackspots

| Action | Contrôle |
|--------|----------|
| **Mode placement** | `B` ou bouton "Click to Place" |
| **Placer nouveau blackspot** | `Left Click` sur terrain (en mode placement) |
| **Déplacer blackspot** | `Left Click + Drag` sur blackspot sélectionné |
| **Ajuster rayon** | `Shift + Scroll Wheel` (blackspot sélectionné) |
| **Ajuster hauteur** | `Ctrl + Scroll Wheel` (blackspot sélectionné) |

### Visualisation

| Action | Raccourci |
|--------|-----------|
| **Toggle wireframe** | `W` |
| **Toggle lighting** | `L` |
| **Toggle mode blackspot** | `B` |

---

## 🎨 INTERFACE UTILISATEUR

### Menu Principal

#### **Menu Map**
- **Load Tile...** - Ouvre dialog pour charger une tile .mmtile spécifique
- **Load Folder...** - Charge toutes les tiles d'un dossier
- **Close Tile** - Ferme la tile courante
- **Exit** - Quitte l'application

#### **Menu Mesh**
- **Load Blackspots...** (`Ctrl+O`) - Charge fichier XML de blackspots
- **Save Blackspots...** (`Ctrl+S`) - Sauvegarde blackspots en XML
- **Export Blackspots (CSV)...** - Exporte pour analyse (format CSV)

#### **Menu View**
- **Wireframe** (`W`) - Affiche/masque les arêtes du navmesh
- **OffMesh Connections** - Affiche/masque les connexions OffMesh
- **Lighting** (`L`) - Active/désactive l'éclairage dynamique
- **Color Mode** → By Area Type / By Height - Change mode de coloration
- **Reset Camera** (`R`) - Recentre la caméra

#### **Menu Tools**
- **Clear Console** - Vide la console de logs

### Panneau Blackspots (Onglet latéral droit)

**Liste des blackspots**
- Affiche tous les blackspots chargés avec format: `Nom (R=rayon)`
- Clic sur un élément → sélection + affichage propriétés
- Double-clic → centrer caméra (pas encore implémenté)

**Propriétés éditables**
- **Name** - Nom du blackspot (TextBox éditable)
- **X, Y, Z** - Coordonnées WoW (NumericUpDown éditables)
  - Range: -20000 à 20000
  - Précision: 0.1
- **Radius** - Rayon du cylindre (1 à 500)
- **Height** - Hauteur du cylindre (1 à 200)

**Boutons**
- **Add** - Ajoute nouveau blackspot à position (0,0,0)
- **Remove** - Supprime le blackspot sélectionné
- **Clear** - Supprime tous les blackspots (avec confirmation)
- **Apply Changes** - Applique modifications des coordonnées manuelles
- **Click to Place** - Active/désactive mode placement par clic
  - Bleu quand actif
  - Curseur devient croix

### Overlay Info (Coin supérieur gauche)

Affiche en temps réel:
```
Pos: {X, Y, Z}           ← Position caméra en coords WoW
Tile: (31, 25)           ← Coordonnées de la tile
Polys: 2,456 | Verts: 4,912  ← Statistiques mesh
Blackspots: 3 | Volumes: 0   ← Nombre d'éléments éditables
FPS: 60 (16.6 ms)        ← Performance
[CLICK MODE - Place Blackspot]  ← Si mode actif
```

### Console (Bas de fenêtre)

Affiche les messages avec codes couleur:
- **Vert** - Succès (LogSuccess)
- **Rouge** - Erreurs (LogError)
- **Orange** - Warnings (LogWarning)
- **Blanc** - Info normale (Log)

---

## 🎯 MODES D'ÉDITION

### Mode Navigation (Par défaut)
- Caméra libre
- Sélection par clic
- Pas de création

### Mode Placement Blackspot (`B` ou bouton)
- Clic gauche sur terrain → crée blackspot
- Animation flash verte (0.8s) au moment de la création
- Retour automatique en mode navigation après création
- Curseur croix
- Overlay affiche "[CLICK MODE - Place Blackspot]"

---

## 🔴 BLACKSPOTS - Détails Techniques

### Qu'est-ce qu'un Blackspot?

Zone cylindrique où le bot ne doit **jamais** aller. Utilisé pour:
- Marquer zones buggées
- Éviter pièges/obstacles
- Bloquer chemins dangereux
- Zones avec mobs agressifs

### Structure Données
```csharp
public struct Blackspot
{
    public Vector3 Location;  // Centre (coordonnées Detour)
    public float Radius;      // Rayon horizontal
    public float Height;      // Hauteur cylindre
    public string Name;       // Nom optionnel
}
```

### Visualisation
- **Cylindre rouge semi-transparent** (alpha 0.4)
- **Jaune/orange** quand sélectionné
- **Vert pulsant** pendant 0.8s après création
- 24 segments pour rondeur
- Caps haut/bas pour fermer le cylindre

### Format de Fichier (XML)
```xml
<?xml version="1.0" encoding="utf-8"?>
<Blackspots>
  <Blackspot X="1234.56" Y="7890.12" Z="345.67" 
             Radius="10.00" Height="10.00" 
             Name="Blackspot 1" />
  <Blackspot X="2345.67" Y="8901.23" Z="456.78" 
             Radius="15.00" Height="20.00" />
</Blackspots>
```

**Compatible Honorbuddy**: ✅ Format identique au .blackspot HB

### Conversions Coordonnées
- **WoW → Detour**: X=-WoW.Y, Y=WoW.Z, Z=-WoW.X
- **Detour → WoW**: X=-Detour.Z, Y=-Detour.X, Z=Detour.Y

**Toutes les coordonnées affichées UI sont en WoW**, mais stockées en Detour en interne.

---

## ⌨️ RACCOURCIS CLAVIER COMPLETS

### Raccourcis Fichiers
| Raccourci | Action |
|-----------|--------|
| `Ctrl+O` | Ouvrir blackspots XML |
| `Ctrl+S` | Sauvegarder blackspots |
| `Ctrl+N` | Clear all blackspots (avec confirmation) |

### Raccourcis Édition
| Raccourci | Action |
|-----------|--------|
| `Delete` | Supprimer blackspot sélectionné |
| `Escape` | Désélectionner |
| `B` | Toggle mode placement blackspot |

### Raccourcis Caméra
| Raccourci | Action |
|-----------|--------|
| `R` | Reset caméra |
| `W` | Toggle wireframe |
| `L` | Toggle lighting |

### Molette Souris (avec modificateurs)
| Modificateur | Action |
|--------------|--------|
| *(aucun)* | Zoom caméra |
| `Shift + Wheel` | Ajuster rayon blackspot sélectionné (±1 par cran) |
| `Ctrl + Wheel` | Ajuster hauteur blackspot sélectionné (±1 par cran) |

---

## 🎨 SYSTÈME DE COULEURS

### Blackspots
- **Rouge** (#FF0000, alpha 0.4) - Normal
- **Jaune/Orange** (#FF CC33, alpha 0.4) - Sélectionné
- **Vert brillant** (#33 FF 33, pulsant) - Flash création (0.8s)

### NavMesh (By Area Type)
- **Vert** - Walkable (Ground)
- **Bleu** - Water
- **Rouge** - Lava / Blocked
- **Jaune** - Road
- **Cyan** - Portal
- **Violet** - Custom areas

### OffMesh Connections
- **Cyan** - Bidirectional
- **Orange** - Unidirectional

---

## 🔧 WORKFLOW TYPIQUE

### Créer des Blackspots

1. **Charger une tile navmesh**
   - Menu Map → Load Tile...
   - Sélectionner fichier `.mmtile`

2. **Activer mode placement**
   - Appuyer sur `B`
   - OU cliquer "Click to Place" dans panneau

3. **Placer blackspots**
   - Cliquer sur terrain où vous voulez le blackspot
   - Flash vert confirme création
   - Répéter pour placer plusieurs

4. **Ajuster propriétés**
   - Sélectionner blackspot (clic dessus)
   - Éditer nom, rayon, hauteur dans panneau
   - OU utiliser `Shift+Wheel` / `Ctrl+Wheel`
   - OU drag pour déplacer

5. **Sauvegarder**
   - `Ctrl+S` ou Menu Mesh → Save Blackspots...
   - Choisir nom fichier

### Éditer Blackspots Existants

1. **Charger fichier**
   - `Ctrl+O` ou Menu Mesh → Load Blackspots...

2. **Sélectionner blackspot**
   - Clic dans liste OU clic dans 3D

3. **Déplacer**
   - Clic+drag dans la vue 3D
   - Suit position curseur sur navmesh

4. **Redimensionner**
   - `Shift+Wheel` pour rayon
   - `Ctrl+Wheel` pour hauteur

5. **Supprimer**
   - Sélectionner puis `Delete`

6. **Sauvegarder modifications**
   - `Ctrl+S`

---

## ⚠️ LIMITATIONS ACTUELLES

### ✅ Implémenté
- [x] Visualisation navmesh multi-tile
- [x] Création blackspots par clic
- [x] Sélection visuelle
- [x] Drag & drop pour déplacer
- [x] Ajustement rayon/hauteur molette
- [x] Load/Save XML format HB
- [x] Animation flash feedback
- [x] Raccourcis clavier complets
- [x] Export CSV

### ❌ Pas Encore Implémenté
- [ ] Jump Links (OffMesh connections manuelles)
- [ ] Convex Volumes (zones polygonales)
- [ ] WMO Blacklist
- [ ] Per-Model Volumes
- [ ] Test pathfinding
- [ ] Raytrace mode
- [ ] Undo/Redo
- [ ] Multi-sélection
- [ ] Snap to grid
- [ ] Copier/coller

---

## 🐛 DÉPANNAGE

### Le fichier .mmtile ne se charge pas
- Vérifier format (MMT + version header)
- Voir console pour erreurs détaillées

### Impossible de cliquer sur blackspot
- Vérifier que mode placement est désactivé (`B`)
- Blackspot peut être caché derrière terrain

### Coordonnées incorrectes après load
- Vérifier conversion WoW↔Detour
- Fichier XML doit utiliser coordonnées WoW

### Crash lors du drag
- Vérifier qu'un navmesh est chargé
- Raycasting nécessite géométrie tile

### Molette ne change pas rayon/hauteur
- Vérifier qu'un blackspot est sélectionné
- Maintenir `Shift` ou `Ctrl` enfoncé
- Range: Radius 1-500, Height 1-200

---

## 📋 COMPARAISON AVEC TRIPPER.RENDERER

| Feature | Tripper.Renderer | MeshViewer3D | Statut |
|---------|------------------|--------------|--------|
| Visualisation navmesh | ✅ | ✅ | 100% |
| Couleurs AreaType | ✅ | ✅ | 100% |
| Wireframe | ✅ | ✅ | 100% |
| OffMesh display | ✅ | ✅ | 100% |
| **Blackspots** | ✅ | ✅ | **100%** |
| - Création par clic | ✅ | ✅ | ✅ |
| - Drag & drop | ✅ | ✅ | ✅ |
| - Ajustement souris | ✅ | ✅ | ✅ |
| - Load/Save XML | ✅ | ✅ | ✅ |
| **Jump Links** | ✅ | ❌ | 0% |
| **Convex Volumes** | ✅ | ❌ | 0% |
| **WMO Blacklist** | ✅ | ❌ | 0% |
| **Test Navigation** | ✅ | ❌ | 0% |
| **Raytrace** | ✅ | ❌ | 0% |
| Undo/Redo | ✅ | ❌ | 0% |

### Priorité Développement Futur
1. **Jump Links** - Connexions OffMesh manuelles (haute priorité)
2. **Convex Volumes** - Zones polygonales (haute priorité)
3. **Undo/Redo** - Histoire modifications (moyenne priorité)
4. **Test Navigation** - Pathfinding visuel (basse priorité)

---

## 🎓 RESSOURCES

### Documentation Technique
- **Recast/Detour**: https://github.com/recastnavigation/recastnavigation
- **OpenTK**: https://opentk.net/
- **NavMesh Format**: Voir MmtileLoader.cs

### Format Fichiers
- **.mmtile**: Format binaire Recast/Detour
- **.blackspot / .xml**: Format texte Honorbuddy
- **.offmesh**: Format binaire connections (pas encore implémenté)
- **.volumes**: Format binaire volumes (pas encore implémenté)

### Coordonnées
- **WoW**: X=North, Y=West, Z=Up
- **Detour**: X=-WoW.Y, Y=WoW.Z, Z=-WoW.X
- **Conversion**: CoordinateSystem.WowToDetour() / DetourToWow()

---

## 📞 SUPPORT

Pour questions/bugs:
1. Vérifier cette documentation
2. Consulter console (messages d'erreur détaillés)
3. Vérifier format fichiers
4. Tester avec tiles de référence

---

**Version**: 1.0.0  
**Date**: Janvier 2026  
**Statut**: Blackspots 100% fonctionnels
