"""Reproducible Natural Earth extraction. Editor-only preparation, no Python runtime dependency.
Install shapely>=2.1 separately; run from repository root.
"""
import hashlib
import json
import pathlib
import sys
import urllib.request

sys.path.insert(0, 'Artifacts/Immersive/geo-tools')
from shapely.geometry import shape
from shapely import constrained_delaunay_triangles

url = 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_0_countries.geojson'
raw = urllib.request.urlopen(url, timeout=90).read()
features = json.loads(raw)['features']
feature = next(f for f in features if f['properties']['ADM0_A3'] == 'FRA')
geometry = shape(feature['geometry'])
# Keep the source's entire country, including overseas polygons. No arbitrary geographic hit boxes.
vertices, triangles, rings = [], [], []
for polygon in geometry.geoms:
    # Preserve topology; < 600 m error is far below a globe's visible texture texel.
    polygon = polygon.simplify(0.005, preserve_topology=True)
    rings.append({'points': [{'x': x, 'y': y} for x, y in polygon.exterior.coords]})
    for triangle in constrained_delaunay_triangles(polygon).geoms:
        start = len(vertices)
        vertices.extend({'x': x, 'y': y} for x, y in list(triangle.exterior.coords)[:3])
        triangles.extend([start, start + 1, start + 2])
root = pathlib.Path('Assets/Alterlife/Immersive/Data')
root.mkdir(parents=True, exist_ok=True)
(root / 'FranceGeometry.json').write_text(json.dumps({'vertices': vertices, 'triangles': triangles, 'rings': rings}, separators=(',', ':')))
(root / 'GeographySource.txt').write_text('France boundaries: Natural Earth 1:10m admin-0, public domain.\nSource: ' + url + '\nSHA256 of downloaded source: ' + hashlib.sha256(raw).hexdigest() + '\nTopology-preserving simplification tolerance: 0.005 degrees. Constrained triangulation includes source holes.\nMap texture: provided Planet Earth Free asset.\n')
print(f'France: {len(rings)} polygons, {len(vertices)} vertices, {len(triangles)//3} triangles')
