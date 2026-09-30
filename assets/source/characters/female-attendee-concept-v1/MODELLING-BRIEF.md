# Female attendee — concept target v1

Status: user/coordinator concept approval pending. ONE cohesive design, generated with the built-in image-generation tool using the approved male concept as style reference and actual female-neutral-v1 as rejected before-context. This is an illustration, not a finished Blender asset or exact orthographic plan. No models changed.

## Design to approve

`female-concept-target.png` shows the proposed three-quarter silhouette, front/side construction guidance, face/hair close-up and rear hair massing. Retain the crafted matte low-poly language, practical teal tee/mustard accent, charcoal trousers and flat dark footwear.

Compared with female-neutral-v1: narrower, less slab-like ribcage and shoulders; natural feminine chest/hip relationship with restrained waist shaping; thinner face and tapered jaw; an off-centre sweep and layered shoulder-skimming hair rather than an extruded bob. Do not rely on hair length alone. This is one adult design, not a universal female body standard.

## Translation into geometry after approval

- Rebuild torso contours and sleeve/shoulder flow rather than scaling the stocky torso uniformly. Cloth should drape over modest chest volume and a gentle waist transition; do not exaggerate the concept into a tiny waist, pointed bust or flared bell hem. Preserve arm clearance and relaxed wrists, without reusing old fixed prop anchors as constraints.
- Use a slim faceted cheek-to-jaw transition and smaller chin. Match the approved visual face proportions rather than forcing a generic anatomical ratio. Keep simple eyes/nose; the image introduces a subtle mouth line, which is guidance only, not a locked new facial-material requirement.
- Hair: compact crown, broad swept front lock, temple framing and roughly 5–7 main layered masses with tapered turned ends. Spend polygons on silhouette breaks and overlap, not tiny strands or painted directional highlights. Resolve front/side/rear inconsistencies into one plausible volume in Blender. Avoid hidden ear poke-through and check shoulder/neck clearance from all angles.
- Tee accent must remain a flush colour region following cloth curvature, never a raised plate. Generated shading is not a requirement for additional textures or dense folds.
- Keep logical hair/body/clothing groups in source to support later modular work, while targeting the established single-mesh, single-material semantic palette export until a different contract is approved. Same 96x8 swatches: skin 0–2, shirt 3–4, accent 5–6, trousers 7–8, dark shoes/eyes 9, hair 10–11. Do not bake skin or hair variation into permanent mesh texture detail.
- Adult scale and roughly seven-to-seven-and-a-half-head appearance should remain consistent with the cast. Do not lock female-v1 shoulder/waist widths, old anchors or every generated view's measurements. Establish actual dimensions against the approved silhouette in the next modelling stage. Tentative 1,200–1,800-triangle planning range, not a measured count or approval to sacrifice silhouette.

## Review cautions and next gate

The generated front, side and hero differ subtly in waist, face and hair treatment; the hero is the primary visual target, with the other views supporting construction. Hair has more visible layering than the existing model and needs actual geometry to earn that appearance. The tee has a fairly fitted waist in the illustration: retain a natural comfortable fit rather than amplifying it in modelling.

Approve the concept before making female Blender v2. No customization suite, poses, rig, production integration or new character batch is included. Previous female model, approved male v5 and approved bar v2 remain unchanged. Prompt and exact reference paths are preserved in `PROMPT.md`.
