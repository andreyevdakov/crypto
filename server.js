var crypto = require('crypto');
var fs = require('fs');

var privatePem = fs.readFileSync('key_rsa.pem');
var publicPem = fs.readFileSync('cert_rsa.pem');
var key = privatePem.toString();
var pubkey = publicPem.toString();

var data = fs.readFileSync('SLBoost.ini');

var sign = crypto.createSign('RSA-SHA512');
sign.update(data);
var sig = sign.sign(key, 'base64');
console.log('signature: ', sig);
fs.writeFileSync('signature.txt', sig);

var verify = crypto.createVerify('RSA-SHA512');
verify.update(data);
var result = verify.verify(pubkey, sig, 'base64');
console.log('verification: ', result);

/*
var pem = fs.readFileSync('key_rsa.pem');
var key = pem.toString('ascii');
console.log(key);
console.log(key.length);
var hmac = crypto.createHmac('sha512', key);

var data = fs.readFileSync('SLBoost.ini');
hmac.update(data);

var sig = hmac.digest('base64');
console.log(sig);

fs.writeFileSync('signature.txt', sig);
*/

