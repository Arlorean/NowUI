// Runs under Node's actual wasm engine against the shipped vector/font objects.
#include "nowui_browser.h"
#include <cstdio>
#include <cstring>
#include <vector>
#include <cstdlib>

static void require(bool condition, const char* message) {
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); }
}
struct Streams {
    float values[9][64] = {};
    int indices[32] = {};
};
static Browser_nowui_vg_blit_mesh meshArgs(Streams& out) {
    static float positions[] = {1,2, 30,4, 9,25};
    static float colors[] = {1,.2f,.3f,1, .1f,.9f,.4f,.8f, .2f,.3f,1,.7f};
    static int indices[] = {0,1,2};
    static float tint[] = {.8f,.7f,.6f,.9f}, mask[] = {0,0,100,100}, rect[] = {1,2,30,40};
    return {positions,colors,3,indices,3,1.25f,4,8,tint,mask,rect,
        out.values[0],out.values[1],out.values[2],out.values[3],out.values[4],
        out.values[5],out.values[6],out.values[7],out.values[8],2,out.indices,3,7};
}
int main() {
    Streams direct, bridged;
    auto a = meshArgs(direct), b = meshArgs(bridged);
    nowui_vg_blit_mesh(a.srcPositions,a.srcColors,a.vertexCount,a.srcIndices,a.indexCount,a.positionScale,
        a.offsetX,a.offsetY,a.tint4,a.mask4,a.rect4,a.dstVerts,a.dstUvs,a.dstRawUv,a.dstRect,a.dstRadius,
        a.dstColor,a.dstOutline,a.dstExtra,a.dstMask,a.dstVertexBase,a.dstIndices,a.dstIndexBase,a.indexOffset);
    nowui_vg_blit_mesh_browser(&b);
    require(std::memcmp(&direct,&bridged,sizeof(direct)) == 0, "mesh exact streams");
    require(bridged.indices[3] == 7 && bridged.values[0][6] != 0, "mesh produced geometry");

    Streams textDirect, textBridged;
    float glyphs[] = {0,0,1,1, .1f,.2f,.3f,.4f, 1.2f,.1f,.2f,1};
    float mask[] = {0,0,100,100}, color[] = {.8f,.7f,.6f,1}, outline[] = {0,0,0,1};
    float penDirect=0, penBridge=0, boundsDirect[4]={}, boundsBridge[4]={};
    int countsDirect[2]={}, countsBridge[2]={};
    Browser_nowui_vg_blit_text_run t{glyphs,0,1,10,20,12,2,mask,color,outline,.3f,8,
        textDirect.values[0],textDirect.values[1],textDirect.values[2],textDirect.values[3],textDirect.values[4],
        textDirect.values[5],textDirect.values[6],textDirect.values[7],textDirect.values[8],1,textDirect.indices,2,
        &penDirect,countsDirect,boundsDirect};
    auto u=t;
    u.dstVerts=textBridged.values[0];u.dstUvs=textBridged.values[1];u.dstRawUv=textBridged.values[2];
    u.dstRect=textBridged.values[3];u.dstRadius=textBridged.values[4];u.dstColor=textBridged.values[5];
    u.dstOutline=textBridged.values[6];u.dstExtra=textBridged.values[7];u.dstMask=textBridged.values[8];
    u.dstIndices=textBridged.indices;u.outPenX=&penBridge;u.outCounts=countsBridge;u.outBounds=boundsBridge;
    nowui_vg_blit_text_run(t.glyphs,t.start,t.end,t.x,t.y,t.fontSize,t.baseline,t.mask4,t.color4,t.outline4,t.outline,
        t.pixelRange,t.dstVerts,t.dstUvs,t.dstRawUv,t.dstRect,t.dstRadius,t.dstColor,t.dstOutline,t.dstExtra,t.dstMask,
        t.dstVertexBase,t.dstIndices,t.dstIndexBase,t.outPenX,t.outCounts,t.outBounds);
    nowui_vg_blit_text_run_browser(&u);
    require(std::memcmp(&textDirect,&textBridged,sizeof(textDirect))==0 && penDirect==penBridge &&
        std::memcmp(countsDirect,countsBridge,sizeof(countsDirect))==0 &&
        std::memcmp(boundsDirect,boundsBridge,sizeof(boundsDirect))==0, "text exact streams and metrics");
    require(countsBridge[0]==4 && countsBridge[1]==6, "text produced glyph quad");

    float packedDirect[256]={},packedBridge[256]={};
    Browser_nowui_vg_pack_canvas c{b.dstVerts,b.dstUvs,b.dstRadius,b.dstRawUv,b.dstColor,b.dstRect,b.dstMask,
        b.dstExtra,b.dstOutline,3,1,2,3,packedBridge,1};
    nowui_vg_pack_canvas(c.srcVerts,c.srcUvs,c.srcRadius,c.srcRawUv,c.srcColors,c.srcRect,c.srcMask,c.srcExtra,
        c.srcOutline,c.vertexCount,c.isText,c.offsetX,c.offsetY,packedDirect,c.dstVertexBase);
    nowui_vg_pack_canvas_browser(&c);
    require(std::memcmp(packedDirect,packedBridge,sizeof(packedDirect))==0,"canvas exact packed vertices");
    std::memset(packedDirect,0,sizeof(packedDirect));std::memset(packedBridge,0,sizeof(packedBridge));
    Browser_nowui_vg_pack_render r{b.dstVerts,b.dstUvs,b.dstRadius,b.dstRawUv,b.dstColor,b.dstRect,b.dstMask,
        b.dstExtra,b.dstOutline,3,2,3,packedBridge,1};
    nowui_vg_pack_render(r.srcVerts,r.srcUvs,r.srcRadius,r.srcRawUv,r.srcColors,r.srcRect,r.srcMask,r.srcExtra,
        r.srcOutline,r.vertexCount,r.offsetX,r.offsetY,packedDirect,r.dstVertexBase);
    nowui_vg_pack_render_browser(&r);
    require(std::memcmp(packedDirect,packedBridge,sizeof(packedDirect))==0,"render exact packed vertices");

    float contour[]={3,0, 0,0,0,0,0,0, 30,0,0,0,0,0, 30,30,0,0,0,0};
    float paint[]={0, .7f,.5f,.3f,1,1, 0,0,0,0, 0,0,0};
    Browser_nowui_vg_stroke s{contour,20,1,nullptr,0,0,0,paint,13,3,1,1,1,.1f,.9f,0,1};
    float strokePositions[8192]={},strokeColors[16384]={},strokeBounds[4]={};int strokeIndices[8192]={};
    int vertices=0,indices=0;
    nowui_vg_begin(.2f,1);
    int strokeResult=nowui_vg_stroke(s.contours,s.contourFloatCount,s.contourCount,s.clipContours,s.clipFloatCount,
        s.clipContourCount,s.clipInvert,s.paint,s.paintFloatCount,s.width,s.cap,s.join,s.hasTrim,s.trimStart,s.trimEnd,
        s.trimOffset,s.trimIndividual);
    nowui_vg_end(&vertices,&indices,strokeBounds);
    require(vertices>0 && indices>0,"stroke produced geometry");
    nowui_vg_copy(strokePositions,strokeColors,strokeIndices,4096,8192);
    float otherPositions[8192]={},otherColors[16384]={},otherBounds[4]={};int otherIndices[8192]={};
    int otherVertices=0,otherIndexCount=0;
    nowui_vg_begin(.2f,1);
    require(nowui_vg_stroke_browser(&s)==strokeResult,"stroke return code");
    nowui_vg_end(&otherVertices,&otherIndexCount,otherBounds);
    nowui_vg_copy(otherPositions,otherColors,otherIndices,4096,8192);
    require(vertices==otherVertices && indices==otherIndexCount &&
        std::memcmp(strokePositions,otherPositions,vertices*2*sizeof(float))==0 &&
        std::memcmp(strokeColors,otherColors,vertices*4*sizeof(float))==0 &&
        std::memcmp(strokeIndices,otherIndices,indices*sizeof(int))==0 &&
        std::memcmp(strokeBounds,otherBounds,sizeof(strokeBounds))==0,"stroke exact tessellation");

    FILE* font=std::fopen("/font.ttf","rb");require(font!=nullptr,"font exists");
    std::fseek(font,0,SEEK_END);int length=std::ftell(font);std::rewind(font);
    std::vector<unsigned char> bytes(length);require(std::fread(bytes.data(),1,length,font)==(size_t)length,"font read");
    std::fclose(font);
    std::vector<unsigned char> atlas(1024*1024*4),otherAtlas(atlas.size());
    unsigned int codepoints[]={65,66};NowMsdfGlyph glyph[2]={},otherGlyph[2]={};NowMsdfAtlasInfo info={},otherInfo={};
    char error[1024]={},otherError[1024]={};
    int fontResult=nowui_compile_font_from_memory_with_codepoints(bytes.data(),length,32,8,codepoints,2,
        atlas.data(),(int)atlas.size(),glyph,2,&info,error,1024);
    Browser_nowui_compile_font_from_memory_with_codepoints f{bytes.data(),length,32,8,(int*)codepoints,2,
        otherAtlas.data(),(int)otherAtlas.size(),otherGlyph,2,&otherInfo,(unsigned char*)otherError,1024};
    require(nowui_compile_font_from_memory_with_codepoints_browser(&f)==fontResult && fontResult==0,"font compile success");
    require(std::memcmp(&info,&otherInfo,sizeof(info))==0 && std::memcmp(glyph,otherGlyph,sizeof(glyph))==0 &&
        std::memcmp(atlas.data(),otherAtlas.data(),info.atlas_byte_count)==0,"font exact atlas and glyph metrics");
    std::puts("PASS: 6 compact native ABI wrappers match stock wasm plugin output exactly");
}
