**Magyar** | [English](../en/versioning.md)

# Verziózás

A dokumentációban használt fogalmak:

- **eredeti:** a [gsinka/servicecomponents](https://github.com/gsinka/servicecomponents) repóból kiadott `ServiceComponents.*` csomagok;
- **Sarkany változat:** az ebből a repóból kiadott `Sarkany.ServiceComponents.*` csomagok.

## A verziószám az eredetit követi

Egy Sarkany kiadás annak az eredeti kiadásnak a verziószámát viseli, amelynek drop-in cseréje. Átálláskor ezért csak a csomag ID-k változnak, a verzió soha:

| Eset | Eredeti | Sarkany |
|---|---|---|
| Első kiadás egy eredeti verzióhoz | `0.2.0-beta.12` | `0.2.0-beta.12` |
| A repó saját javításai ugyanazon a kompatibilitási szinten | | `0.2.0-beta.12.1`, `0.2.0-beta.12.2`, … |
| Az eredeti új kiadást ad ki, és ezt átvesszük | `0.2.0-beta.13` | `0.2.0-beta.13` |
| A stabil verziók ugyanígy | `1.0.0` | `1.0.0`, javítások: `1.0.0.1`, … |

A plusz szám miatt a NuGet sorrendje helyes marad: `0.2.0-beta.12` < `0.2.0-beta.12.1` < `0.2.0-beta.13`, illetve `1.0.0` < `1.0.0.1` < `1.0.1`. Egy `-sarkany.1` jellegű utótag nem működne: a NuGet az `1.0.0-sarkany.1`-et az `1.0.0` elé sorolja.

Az assembly-verzió a verzió numerikus része (`0.2.0-beta.12` és `0.2.0-beta.12.1` esetén `0.2.0.0`, `1.0.0.1` esetén `1.0.0.1`), ugyanannál a kiadásnál megegyezik az eredetiével.

## A kompatibilitási verzió: `versions.props`

A repó gyökerében lévő `versions.props` adja meg, melyik eredeti kiadással kompatibilis a repó:

| Property | Jelentés |
|---|---|
| `UpstreamVersion` | Az eredeti kiadás (jelenleg `0.2.0-beta.12`). Az interop és a teljesítménymérés ehhez hasonlít; a helyi buildek ezt kapják verzióként. |
| `UpstreamIgnoredVersions` | Eredeti kiadások, amelyeket soha nem kell követni, `;`-vel elválasztva. A `0.2.1-beta.1` szerepel benne: régebbi commitból készült, mint a `0.2.0-beta.12`, pedig a verziója nagyobbnak számít. |

A `bash build/version.sh upstream` kiírja az `UpstreamVersion` értékét, a `bash build/version.sh release <tag>` pedig ellenőriz egy kiadási taget.

## Kiadás

Egy `<UpstreamVersion>` vagy `<UpstreamVersion>.N` taget kell pusholni (az elején `v` is lehet). A CD workflow (`.github/workflows/cd.yml`) minden más taget elutasít, lefuttat minden ellenőrzést, majd kiadja a csomagokat a nuget.org-ra, és GitHub release-t készít.

## Egy új eredeti kiadás követése

Az eredeti repó folyamatosan változik. A heti `.github/workflows/upstream.yml` workflow megkeresi a nuget.org-on az `UpstreamVersion`-nél újabb eredeti kiadásokat, mindegyiket összeveti ezzel a repóval, és issue-t nyit róla. Egy kiadás követése:

1. Át kell venni az eredeti kiadás változásait (új funkciók, javítások).
2. Ellenőrzés: `bash build/upstream-check.sh <verzió>`
   - **publikus API:** az ApiCompat jelez mindent, ami az eredeti publikus API-jából (típusok, tagok, paraméternevek, assembly-verzió) itt hiányzik vagy megváltozott; a bővítés megengedett;
   - **interop:** az abból a kiadásból és ebből a repóból buildelt példaszolgáltatások egymással futnak (Docker kell hozzá; a `--no-interop` kihagyja).
3. A `versions.props`-ban az `UpstreamVersion` értékét a kiadásra kell állítani, és frissíteni kell a [migrációs jegyzetet](migration/from-original.md) és a CHANGELOG-ot.
4. Kiadás a `<verzió>` taggel.

A `bash build/upstream-check.sh --list-new` kiírja a még nem követett eredeti kiadásokat. Ha egy kiadást nem kell követni, az `UpstreamIgnoredVersions`-be kell felvenni.

Egy Sarkany verzió soha nem előzheti meg az eredeti következő verzióját: a `0.2.0-beta.13` csak akkor adható ki, ha lefedi az eredeti `0.2.0-beta.13`-at.
