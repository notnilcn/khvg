// https://observablehq.com/@sanderevers/hexmod-representation@842
import define1 from "./47e466502e507073@1681.js";
import define2 from "./a46446d8701dfc65@35.js";

function _1(md){return(
md`# Hexmod representation`
)}

function _2(md){return(
md`Suppose you have assembled [small hexes into big hexes](https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid) and you want to represent the position of a small hex relative to the big hex it is contained in; for example, because you want to create a pattern that repeats across the big hexes. A natural choice for this would be to use its small-hex coordinates *relative to the center* of its big hex:`
)}

function _3(small_to_big,d3,my,hexmod,hexgrid,width,rel_coords)
{ 
  function col(xyz) {
    const [i,j,k] = small_to_big(xyz);
    const orange = d3.color("orange");
    orange.opacity = [0,0.4,0.8][my.mod(i-j,3)];
    return orange.toString();
  }
  function opa(xyz) {
    const [i,j,k] = small_to_big(xyz);
    const d = Math.max(i,0)+Math.max(j,0)+Math.max(k,0);
    return 1/(d*2);
  }
  const bold_center = xyz => hexmod(xyz)==0 ? "bold" : null;
    
  
  const {svg,center_text,corner_text,hexagon,hexaside} = hexgrid({height:300,width,unit_space:40});
  hexagon().attr("fill",col).attr("stroke","black").attr("opacity",opa);
  corner_text(5,0.4).attr("opacity",opa).attr("font-weight",bold_center).text(xyz => rel_coords(xyz)[0]);
  corner_text(1,0.4).attr("opacity",opa).attr("font-weight",bold_center).text(xyz => rel_coords(xyz)[1]);
  corner_text(3,0.4).attr("opacity",opa).attr("font-weight",bold_center).text(xyz => rel_coords(xyz)[2]);

  return svg.node();
}


function _rel_coords(small_to_big,center_of){return(
function rel_coords(xyz) {
  const [x,y,z] = xyz;
  const ijk = small_to_big(xyz)
  const [xc,yc,zc] = center_of(ijk);
  return [x-xc,y-yc,z-zc];
}
)}

function _center_of(r){return(
function center_of(ijk) {
  const [i,j,k] = ijk;
  //const c = my.add(my.scale([2*r+1,-r,-r-1],i), my.scale([r,r+1,-2*r-1],j));
  return [(r+1)*i - r*k, (r+1)*j - r*i, (r+1)*k - r*j];
}
)}

function _6(md){return(
md`These relative cube coordinates are familiar to work with, and useful if you want to do rotation around the big hex center, mirroring across the three big hex axes, or calculation of distance to center.

But there are different choices available.`
)}

function _7(md){return(
md`## The hexmod representation`
)}

function _8(md){return(
md`In the \`hexmod\` representation, the small hexes within a big hex are represented by just one integer:`
)}

function _9(r,hexgrid,small_to_big,hexmod)
{
  const unit_space = 30;
  const bound = unit_space * (2*r+1);
  const {svg,center_text,corner_text,cc2xy,hexagon,hexaside} = hexgrid({height:bound*0.9,width:bound,unit_space});
  const chm = (val,els) => xyz => small_to_big(xyz).join('').startsWith('000')?val:els;
  
  hexagon(0.9).attr("fill",chm("purple","white"));
  center_text().attr("fill","white").text(xyz=>chm(hexmod(xyz))(xyz));
  return svg.node()
}


function _10(md,tex,r,A){return(
md`Each hex gets a unique ${tex`m`} value (${tex`0 \leq m < A`}), where ${tex`A = 3r^2+3r+1`} is the area of the big hex. In the examples on this page, ${tex`r=${r}`} and ${tex`A=${A}`}. There are various ways to assign the ${tex`m`} values; the one shown here is the result of the following function:`
)}

function _hexmod(my,s,A){return(
function hexmod(xyz) {
  const [x,y,z]=xyz;
  const m = my.mod(y+s*x, A);
  return m;
}
)}

function _12(md,tex){return(
md`Here, \`my.mod\` is the modulo function (a.k.a. the *remainder* of integer division); ${tex`s = 3r+2`} is a constant related to the size of the big hex (called \`shift\` in the [other notebook](https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid)).`
)}

function _13(md){return(
md`A remarkable property of this \`hexmod\` function is that it works directly on absolute cube coordinates; we don't need to determine the containing big hex first, like \`rel_coords\` does. (Note the similarity to the auxiliary \`xh\` value in the \`small_to_big\` function. In \`yh\` and \`zh\`, we could also replace \`div\` by \`mod\`, yielding alternative variants for \`hexmod\`.)`
)}

function _14(md,tex){return(
md`The fact that \`hexmod\` maps the hexes to a contiguous range of numbers makes it useful for *storing* a pattern (for example, of colors) that repeats across the big hexes. The pattern is stored in an array \`patt\` of size ${tex`A`}; a small hex with coordinates \`xyz\` is then given the color \`patt[hexmod(xyz)]\`. Here's an interactive demo (click the squares in the array):`
)}

function _grid_hexmod(patt,hexmod,my,shift,r,hexgrid,width,html,$0,invalidation,bigside)
{ 
  const choose_color = xyz => patt[hexmod(xyz)]?"fuchsia":"#eeeeee";
  
  function sixth(xyz) {
    const m = hexmod(xyz);
    //return my.div(my.mod(m-1,shift)+1,r+1);
    const tm = my.mod(m-1,shift),
          td = my.div(m-1,shift);
    const h1 = my.div(tm+1,r+1);
    const h2 = my.div(my.mod(m+2*r,shift-1)+1,r+1);
    return h1*2+h2;
    
  }
  
  const {svg,center_text,corner_text,cc2xy,hexagon,hexaside} = hexgrid({height:350,width,unit_space:25});
  const caption = `The <code>hexmod</code> value of each small hex, and the color of the corresponding element of the array.` 
  const outer = html`${svg.node()}<div style="text-align:center"><i>${caption}</i></div>`;
  
  function report(xyz) {
    const [x,y,z]=xyz;
    const [ox,oy,oz] = outer.value
    if (ox != x || oy !=y) {
      outer.value=xyz;
      outer.dispatchEvent(new CustomEvent("input"));
    }
  }
  const cursor = svg.append("circle")
    .data([[0,0,0]])
    .attr("cx",xyz=>cc2xy(xyz)[0])
    .attr("cy",xyz=>cc2xy(xyz)[1])
    .attr("r",12)
    .attr("stroke","orange").attr("stroke-width",2).attr("fill","none");
  const drawCursor = () => cursor.data([outer.value])
    .attr("cx",xyz=>cc2xy(xyz)[0]).attr("cy",xyz=>cc2xy(xyz)[1]);
  $0.addEventListener("input",drawCursor);
  invalidation.then(() => $0.removeEventListener("input",drawCursor));
            
  hexagon().attr("fill",choose_color).attr("stroke","white").on("mouseover",report);
  center_text().text(hexmod).on("mouseover",report);
  hexaside(0).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(0,"black"));
  hexaside(2).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(2,"black"));
  hexaside(4).attr("stroke-width",3).attr("stroke-linecap","round").attr("stroke",bigside(4,"black"));

  return outer;
}


function _matrix_hexmod(d3,width,r,html,inv_hexmod,patt,$0,area,my,shift,hexmod,model_m)
{
  const unit=25;
  const svg = d3.create("svg")
    .attr("width", width)
    .attr("height", unit*(r+1));
  
  const caption = `Array <code>patt</code> of 1-bit colors that stores the repeating pattern.` 
  const outer = html`${svg.node()}<div><i>${caption}</i></div>`;

  function report(p) {
    const [x,y,z]=inv_hexmod(p);
    const [ox,oy,oz] = outer.value
    if (ox != x || oy !=y) {
      outer.value=[x,y,z];
      outer.dispatchEvent(new CustomEvent("input"));
    }
  }
  
  function flip(p) {
    const mcc = [...patt];
    mcc[p]=1-mcc[p]; 
    $0.value=mcc;
  }
  
  const boxes = svg
    .selectAll("rect")
    .data(d3.range(area))
    .join("rect")
    .attr("x", p => my.mod(p,shift)*unit)
    .attr("y", p => my.div(p,shift)*unit)
    .attr("height", unit).attr("width",unit)
    .attr("fill",p=>patt[p]?"fuchsia":"#eeeeee").attr("stroke","white")
    .on("mouseover",report)
    .on("click",flip);
  
  const p = hexmod(model_m);
  svg.append("rect")
    .attr("x",my.mod(p,shift)*unit)
    .attr("y",my.div(p,shift)*unit)
    .attr("height", unit).attr("width",unit)
    .attr("fill","none").attr("stroke","orange").attr("stroke-width",2);
  
  const numbers = svg.selectAll("text").data(d3.range(area)).join("text")
    .attr("x", p => my.mod(p,shift)*unit + unit/2)
    .attr("y", p => my.div(p,shift)*unit + unit/2)
    .attr("text-anchor","middle")
    .style("dominant-baseline","middle")
    .style("font-family","sans-serif")
    .style("font-size","10pt")
    .text(p=>p)
    .on("mouseover",report)
    .on("click",flip);
  
  return outer;
}


function _17(md){return(
md`## Translation and rotation in hexmod representation`
)}

function _18(md,tex){return(
md`The \`hexmod\` representation has the following nice property (which follows from the linearity of the function):
> If we start at a position with number ${tex`m`} and move by a vector ${tex`(dx,dy,dz)`}, we end up at a position with number ${tex.block`m + \mathrm{hexmod}(dx,dy,dz) \pmod A`}
`
)}

function _19(md){return(
md`For example, moving by`
)}

function _dx(){return(
2
)}

function _dy(){return(
1
)}

function _dz(dx,dy){return(
-(dx+dy)
)}

function _23(md,hexmod,dx,dy,dz){return(
md`adds ${hexmod([dx,dy,dz])}. This property also "[wraps around](https://www.redblobgames.com/grids/hexagons/#wraparound)" the edges of the big hexes.`
)}

function _24(hexgrid,width,html,dx,dy,dz,hexmod,tex,A,my,bigside)
{ 
  const {svg,center_text,corner_text,cc2xy,hexagon,hexaside} = hexgrid({height:250,width,unit_space:25});
  const caption = html`Translation by (${dx},${dy},${dz}) is adding ${hexmod([dx,dy,dz])} ${tex`\pmod{${A}}`}.` 
  const outer = html`${svg.node()}<div style="text-align:center"><i>${caption}</i></div>`;
  
  const circleF = svg.append("circle").attr("r",12)
    .attr("stroke","orange").attr("stroke-width",2).attr("fill","none");
  const circleT = svg.append("circle").attr("r",12).attr("cx",cc2xy([dx,dy])[0]).attr("cy",cc2xy([dx,dy])[1])
    .attr("stroke","blue").attr("stroke-width",2).attr("fill","none");
  
  const drawCircles = (xyz) => {
    const [xF,yF] = cc2xy(xyz);
    const [xT,yT] = cc2xy(my.add(xyz,[dx,dy,dz]));
    circleF.attr("cx",xF).attr("cy",yF);
    circleT.attr("cx",xT).attr("cy",yT);
  }  
            
  hexagon().attr("fill","lightgrey").attr("stroke","white").on("mouseover",drawCircles);
  center_text().text(hexmod).on("mouseover",drawCircles);
  hexaside(0).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(0,"white"));
  hexaside(2).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(2,"white"));
  hexaside(4).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(4,"white"));

  return outer;
}


function _25(md,tex){return(
md`A more suprising property of the \`hexmod\` function is this:

> If we start at a position with number ${tex`m`} and do a clockwise rotation by 60º around the center of the big hex, we end up at a position with number ${tex`m \cdot s \pmod A`}. A counterclockwise rotation takes us to number ${tex`m \cdot s^{-1} \pmod A`}, where ${tex`s^{-1} = 3r^2`}.`
)}

function _26(hexgrid,width,html,s,tex,A,hexmod,bigside)
{ 
  const {svg,center_text,corner_text,cc2xy,hexagon,hexaside} = hexgrid({height:250,width,unit_space:25});
  const caption = html`Clockwise rotation by 1/6 is multiplication by ${s} ${tex`\pmod{${A}}`}.` 
  const outer = html`${svg.node()}<div style="text-align:center"><i>${caption}</i></div>`;
  
  const circleF = svg.append("circle").attr("r",12)
    .attr("stroke","orange").attr("stroke-width",2).attr("fill","none");
  const circleT = svg.append("circle").attr("r",12)
    .attr("stroke","blue").attr("stroke-width",2).attr("fill","none");
  
  const drawCircles = (xyz) => {
    const [xF,yF] = cc2xy(xyz);
    const [x,y,z] = xyz;
//    const [xr,yr,zr] = rel_coords(xyz);
//    const [xc,yc,zc] = center_of(small_to_big(xyz));                                 
//    const [xT,yT] = cc2xy([xc-zr,yc-xr,zc-yr]);
    const [xT,yT] = cc2xy([-z,-x,-y]);
    circleF.attr("cx",xF).attr("cy",yF);
    circleT.attr("cx",xT).attr("cy",yT);
  }  
            
  hexagon().attr("fill","lightgrey").attr("stroke","white").on("mouseover",drawCircles);
  center_text().text(hexmod).on("mouseover",drawCircles);
  hexaside(0).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(0,"white"));
  hexaside(2).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(2,"white"));
  hexaside(4).attr("stroke-width",4).attr("stroke-linecap","round").attr("stroke",bigside(4,"white"));

  return outer;
}


function _27(md,tex){return(
md`In particular, since the number 1 is located in the ring of six hexes directly around the center, this ring consists of powers of ${tex`s \pmod A`}: ${tex`s^0,s^1,s^2,s^3,s^4,s^5`}, clockwise. These can be written in terms of ${tex`r`}:

${tex.block`\begin{aligned}
    s^0 &\equiv 1\\
    s^1 &\equiv 3r+2\\
    s^2 &\equiv 3r+1\\
    s^3 &\equiv 3r^2+3r \equiv -1\\
    s^4 &\equiv 3r^2-1 \equiv -s\\
    s^5 &\equiv 3r^2 \equiv -s^2
  \end{aligned}`}`
)}

function _28(md,tex,r,s,A){return(
md`For ${tex`r=${r}`}, ${tex.block`s^0, \ldots, s^5 \equiv 1, ${s}, ${s-1}, ${A-1}, ${A-s}, ${A-s+1}`}`
)}

function _29(hexgrid,hexmod)
{
  const unit_space = 33;
  const bound = unit_space * 3;
  const {svg,center_text,corner_text,cc2xy,hexagon,hexaside} = hexgrid({height:bound,width:bound,unit_space});
  const chm = (val,els) => xyz => {
    const [x,y,z] = xyz;
    return ((Math.abs(x)+Math.abs(y)+Math.abs(z))<=2)?val:els;
  }
  
  hexagon(0.9).attr("fill",chm("navy","white"));
  center_text().attr("fill","white").text(xyz=>chm(hexmod(xyz))(xyz));
  return svg.node()
}


function _30(md,tex){return(
md`The rotation property is not difficult to prove. In cube coordinates, clockwise [rotation](https://www.redblobgames.com/grids/hexagons/#rotation) of ${tex`x,y,z`} yields new coordinates ${tex`-z,-x,-y`}; then

${tex.block`\begin{aligned}
&\textrm{hexmod}(-z,-x,-y)\\
=& -sz-x \\
=& s(x+y)-x\\ 
=& (s-1)x+sy \\
\equiv& s^2 x + sy \\
=& s \cdot \textrm{hexmod}(x,y,z)
\end{aligned}`}`
)}

function _31(md){return(
md`## Back to cube coordinates`
)}

function _32(md,tex){return(
md`To recover relative cube coordinates from ${tex`m`}, you can use:`
)}

function _inv_hexmod(my,r,s){return(
function inv_hexmod(m) {
  const ms = my.div(m+r,s),
        mcs = my.div(m+2*r,s-1);
  const x = ms*(r+1) + mcs*-r,
        y = m + ms*(-2*r-1) + mcs*(-r-1),
        z = -m + ms*r + mcs*(2*r+1);
  return [x,y,z];
}
)}

function _34(md){return(
md`## Appendix`
)}

function _model_m(Inputs){return(
Inputs.input([0,0,0])
)}

function _patt(d3,area,r){return(
d3.range(area).map(x => x<=2*r+2?1:0)
)}

function _37(Inputs,grid_hexmod,$0,matrix_hexmod)
{ 
  Inputs.bind(grid_hexmod,$0); Inputs.bind(matrix_hexmod,$0);
}


function _r(){return(
3
)}

function _s(shift){return(
shift
)}

function _A(area){return(
area
)}

export default function define(runtime, observer) {
  const main = runtime.module();
  main.variable(observer()).define(["md"], _1);
  main.variable(observer()).define(["md"], _2);
  main.variable(observer()).define(["small_to_big","d3","my","hexmod","hexgrid","width","rel_coords"], _3);
  main.variable(observer("rel_coords")).define("rel_coords", ["small_to_big","center_of"], _rel_coords);
  main.variable(observer("center_of")).define("center_of", ["r"], _center_of);
  main.variable(observer()).define(["md"], _6);
  main.variable(observer()).define(["md"], _7);
  main.variable(observer()).define(["md"], _8);
  main.variable(observer()).define(["r","hexgrid","small_to_big","hexmod"], _9);
  main.variable(observer()).define(["md","tex","r","A"], _10);
  main.variable(observer("hexmod")).define("hexmod", ["my","s","A"], _hexmod);
  main.variable(observer()).define(["md","tex"], _12);
  main.variable(observer()).define(["md"], _13);
  main.variable(observer()).define(["md","tex"], _14);
  main.variable(observer("grid_hexmod")).define("grid_hexmod", ["patt","hexmod","my","shift","r","hexgrid","width","html","viewof model_m","invalidation","bigside"], _grid_hexmod);
  main.variable(observer("matrix_hexmod")).define("matrix_hexmod", ["d3","width","r","html","inv_hexmod","patt","mutable patt","area","my","shift","hexmod","model_m"], _matrix_hexmod);
  main.variable(observer()).define(["md"], _17);
  main.variable(observer()).define(["md","tex"], _18);
  main.variable(observer()).define(["md"], _19);
  main.variable(observer("dx")).define("dx", _dx);
  main.variable(observer("dy")).define("dy", _dy);
  main.variable(observer("dz")).define("dz", ["dx","dy"], _dz);
  main.variable(observer()).define(["md","hexmod","dx","dy","dz"], _23);
  main.variable(observer()).define(["hexgrid","width","html","dx","dy","dz","hexmod","tex","A","my","bigside"], _24);
  main.variable(observer()).define(["md","tex"], _25);
  main.variable(observer()).define(["hexgrid","width","html","s","tex","A","hexmod","bigside"], _26);
  main.variable(observer()).define(["md","tex"], _27);
  main.variable(observer()).define(["md","tex","r","s","A"], _28);
  main.variable(observer()).define(["hexgrid","hexmod"], _29);
  main.variable(observer()).define(["md","tex"], _30);
  main.variable(observer()).define(["md"], _31);
  main.variable(observer()).define(["md","tex"], _32);
  main.variable(observer("inv_hexmod")).define("inv_hexmod", ["my","r","s"], _inv_hexmod);
  main.variable(observer()).define(["md"], _34);
  main.variable(observer("viewof model_m")).define("viewof model_m", ["Inputs"], _model_m);
  main.variable(observer("model_m")).define("model_m", ["Generators", "viewof model_m"], (G, _) => G.input(_));
  main.define("initial patt", ["d3","area","r"], _patt);
  main.variable(observer("mutable patt")).define("mutable patt", ["Mutable", "initial patt"], (M, _) => new M(_));
  main.variable(observer("patt")).define("patt", ["mutable patt"], _ => _.generator);
  main.variable(observer()).define(["Inputs","grid_hexmod","viewof model_m","matrix_hexmod"], _37);
  const child1 = runtime.module(define1).derive(["r"], main);
  main.import("small_to_big", child1);
  main.import("bigside", child1);
  main.import("area", child1);
  main.import("shift", child1);
  main.variable(observer("r")).define("r", _r);
  main.variable(observer("s")).define("s", ["shift"], _s);
  main.variable(observer("A")).define("A", ["area"], _A);
  const child2 = runtime.module(define2);
  main.import("hexgrid", child2);
  main.import("my", child2);
  main.import("d3", child2);
  return main;
}
