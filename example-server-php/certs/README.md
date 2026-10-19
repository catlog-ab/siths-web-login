# SITHS CA-certifikat

`siths-ca-bundle.pem` ska innehålla, i PEM-format:

1. **Utfärdande CA** för HSA-id-certifikaten, i dag *SITHS e-id Person HSA-id 3 CA v1*. Adressen står i kortcertifikatets AIA-fält: <http://aia.siths.se/sithseidpersonhsaid3cav1.cer>.
2. **SITHS rot-CA** som har utfärdat ovanstående. Den hämtas från Ineras sida om SITHS-certifikat (inera.se).

Kontrollera certifikatens fingeravtryck mot de värden som Inera publicerar. Konvertera vid behov från DER:

```
openssl x509 -inform DER -in sithseidpersonhsaid3cav1.cer -out issuing.pem
cat issuing.pem root.pem > siths-ca-bundle.pem
```
