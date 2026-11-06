# Handoff: F4 · agotamiento de motores dentro del paso

**Estado:** PR #18, borrador, sin mergear. Rama `claude/refine-local-plan-90r917`, head `f1a3165`, base `main` en `ccbc5c9`. El CI de GitHub Actions está en verde en el push y en el PR, incluido el smoke de Godot. No tiene conflictos ni revisiones.
https://github.com/sebastianvelace/Exosphere/pull/18

## Reglas del dueño (obligatorias)

- Hacer commit solo como `Sebas <sebastianvelace@gmail.com>`, **sin** trailer `Co-Authored-By` ni `Claude-Session`. La config local de git dice "Claude": no usarla. Usar:
  `GIT_AUTHOR_NAME="Sebas" GIT_AUTHOR_EMAIL="sebastianvelace@gmail.com" GIT_COMMITTER_NAME="Sebas" GIT_COMMITTER_EMAIL="sebastianvelace@gmail.com" git commit ...`
- Hacer commit solo con `bash tools/ci_check.sh` en verde (unos 20 min; la suite completa lleva casi todo ese tiempo).
- Realismo máximo. **Nunca** ampliar tolerancias para tapar física. Todo dato no publicado necesita su registro en `data/provenance/*.json`.
- No commitear `_Diag*.cs`, harnesses de captura ni `bin/`, `obj/`, `.godot/`, `*.uid`.
- Comunicarse en español con el usuario.

## Contexto previo

- F0, F1, F2 y F3 están cerrados en `main`. F3 cerró en `d7fb773`: control de actitud solo por actuadores, con RCS declarado por pieza.
- Ya estaban aceptados en `main`, hechos por otro agente:
  - fundamentos de torque de F4 (`14349dd`);
  - gradiente gravitatorio y actitud Kepler (`44b47b1`);
  - financiamiento atómico del RCS con recibos (`04bffd6`).
- Otro agente dejó la rama WIP `codex/f4-engine-exhaustion` (`f1868ef`) sin validar. Mi trabajo parte de ese commit y lo completa. El PR #18 contiene `f1868ef` más mi commit `f1a3165`.

## Qué implementa la unidad

Vista física (el código del WIP lo revisé y lo mantuve):

1. **`PartGraph.ConsumePropellantFromPool`**
   - Suma la demanda del paso de todos los motores líquidos que comparten depósito. El orden ordinal por ID es solo de suma, no da prioridad.
   - Limita la quema común por el **primer reactivo que se agota**, debita solo lo financiado y **conserva el reactivo sobrante**.
   - Antes, el código tiraba ambos reactivos restantes en el último paso: era masa perdida sin empuje.
2. **`PartGraph.EngineFunding.cs`** (nuevo)
   - Guarda las fronteras pendientes (motor, duración financiada) durante el tick.
   - El motor se marca agotado en `EndPhysicsTick`, **después** de integrar su impulso. Fuera de un tick se agota en el acto.
   - `IntegrateEngineIntervals` parte el paso en cada frontera: cada tramo es un RK4 completo con un conjunto fijo de motores vivos, controlado por `Part.FundingThrustScale` (vale 1 o 0).
   - `GetFundedAngularAcceleration` hace que el Euler de actitud legacy integre el impulso angular financiado una sola vez.
3. **`Universe`**: usa el helper en los caminos acoplado, legacy y de agua. La traslación legacy ahora corre con el tick abierto (`BeginLegacyPhysicsStep` / `EndCoupledPhysicsStep`).
4. **Sin cambios:**
   - depósitos por etapa, depósitos de hot-staging, headers después de los tanques principales, reserva de aterrizaje protegida;
   - el RCS sigue financiando el paso completo;
   - masa e inercia siguen congeladas por paso;
   - motores sólidos y monopropelente siguen con agotamiento de paso completo.
5. **Propelente infinito:** mantiene su contrato anterior. Se calcula el consumo bruto y luego se restauran las cargas; un tanque casi vacío con la asistencia activa igual corta.

## Lo que agregué yo (commit `f1a3165`)

- **`ExosphereSimulation.Tests/EngineExhaustionTests.cs`**: 15 casos, en ambos integradores.
  - Ya existían: retención del reactivo sobrante e impulso de paso parcial (incluye posición `a·t_f·(dt − t_f/2)` y que el paso siguiente no agregue impulso).
  - Nuevos:
    - agotamiento exacto;
    - depósito vacío (sin impulso y sin tirar oxidante);
    - dos motores distintos que comparten depósito y cortan juntos;
    - etapas de hot-staging que cortan en instantes distintos;
    - headers que extienden la quema sin tocar la reserva protegida;
    - motor fuera del eje cuyo momento actúa solo en su intervalo (tensor de inercia en el camino acoplado, inercias escalares en el legacy).
  - Contra el código anterior (`ccbc5c9`), 13 de los 15 fallan. Los 2 de agotamiento exacto pasan en ambos, como corresponde.
- **`Flight14BoosterReturnTests`**
  - El boostback del Super Heavy termina por falta de LOX y ahora **conserva 1011 kg de metano** sin oxidante que antes se quemaba.
  - El test aceptaba "reserva + hasta 1000 kg". Lo cambié por la identidad exacta: `corte = reserva + LF abierto − Ox abierto / MR`, con tolerancia de 1 g. **No** amplié tolerancias.
- **`tools/tests/engine_failure_disabled_contract_test.sh`**: ahora busca la frontera en `PartGraph.EngineFunding.cs` (`RegisterEngineExhaustion(demand.EnginePart` + `entry.Engine.MarkFuelDepleted();`). No lo relajé.
- **Documentación:**
  - `docs/physics/variable_mass_resource_adapter.md`, sección "Implemented engine exhaustion boundary";
  - `docs/physics/rigid_body_fidelity_plan.md`, en el progreso de A6;
  - `ROADMAP.md`.

## Validación

- `tools/ci_check.sh` local: **1331/1331** tests, 3 builds con 0 warnings / 0 errors y todos los contratos estáticos OK. No hay binario de Godot en este entorno; el smoke de Godot pasó en GitHub Actions.
- Ningún otro test, tolerancia ni calibración cambió.

## Pendiente (siguiente trabajo de F4)

1. Mergear el PR #18 cuando el dueño lo apruebe (está en borrador).
2. Muestreo continuo de masa, centro de masa e inercia dentro del paso. Hoy queda congelado con la masa ya debitada. Las fórmulas están en `variable_mass_resource_adapter.md`.
3. Recibos de motores y venteos, análogos a los recibos de RCS (`Vessel.CaptureRcsFunding`), que expongan el intervalo financiado.
4. Fuerzas de transporte de masa variable (jet damping), con la convención de volumen de control del documento.
5. Gate histórico de jet damping del Saturn V. **No** usar el amortiguamiento del actuador SPS para cerrarlo.
6. Opcional: agotamiento por intervalo también para el RCS y para motores sólidos y monopropelente.

## Comandos útiles

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --filter "FullyQualifiedName~EngineExhaustionTests"
bash tools/ci_check.sh          # obligatorio antes de commitear
bash tools/fidelity_gate.sh F3  # gates por fase
```
