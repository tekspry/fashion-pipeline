now identify the exact color and describe precise design features for this newly uploaded {{ACCESSORY_TYPE}} image (button, lace, border, trim, brooch, or embellishment) generally applied on front of ladies kurti, ladies suits of Indian ladies dress. Inspect the image closely and extract exact material, color, surface texture, silhouette, profile, and craftsmanship details.

Return ONLY a JSON object containing the following keys:
- Title: A striking, descriptive name capturing the exact color, material, pattern/silhouette, and dimension (e.g. "1-Inch Textured Champagne Gold Starfish Statement Button" or "2.5-Inch Champagne Gold Fish-Scale Mirror Border").
- ColorIdentification: An object with keys:
    - PrimaryFinish: Exact primary finish, color (e.g. Polished Champagne Gold / Light Gold), and luster/metallic shine.
    - ReflectiveUndertones: Shadow undertones, reflectivity, and secondary highlights (e.g. Smoky Gunmetal / Deep Bronze shadows).
    - BaseMaterial: Foundation material, plating, or thread.
- PreciseDesignFeatures: An object with keys describing key design and structural attributes (e.g. ProportionalProfile, Silhouette, Texture, Perimeter, Form, BackingOrShank, Craftsmanship).
- SuggestedApplications: An array of strings covering specific Indian ladies dress applications (e.g. Kurti Front Plackets, Sleeve Cuff Accents, Necklines, Saree Borders, Dupatta Framing).
- Color: Summary string of exact colors and tones.
- Type: Summary string of exact type and dimension.
- Material: Summary string of materials, finish, and texture.
- Vibe: Summary string of aesthetic vibe.
- Style: Summary string of styling compatibility.
- Features: Detailed bulleted or paragraph summary of all precise design features.
